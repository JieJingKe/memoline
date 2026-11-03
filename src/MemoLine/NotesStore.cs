using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace MemoLine;

/// <summary>
/// notes.json 的唯一读写入口：400ms 防抖、临时文件 + 原子替换、损坏文件改名备份。
/// 所有调用都在 UI 线程（DispatcherTimer），因此列表本身不需要加锁。
/// </summary>
internal sealed class NotesStore : IDisposable
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    private readonly List<NoteItem> _notes = [];
    private readonly DispatcherTimer _debounce;

    public NotesStore(string path)
    {
        Path = path;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            SaveNow();
        };
    }

    public string Path { get; }

    public IReadOnlyList<NoteItem> Notes => _notes;

    public int Count => _notes.Count;

    public bool CanAdd => _notes.Count < Metrics.MaxNotes;

    /// <summary>损坏文件改名后的路径，用于启动提示一次。</summary>
    public string? CorruptBackupPath { get; private set; }

    public string? LoadError { get; private set; }

    /// <summary>连备份都写不出去时置为只读，避免下一次保存覆盖掉原始证据。</summary>
    public bool ReadOnly { get; private set; }

    public void Load()
    {
        _notes.Clear();
        CorruptBackupPath = null;
        LoadError = null;
        ReadOnly = false;

        if (!File.Exists(Path))
        {
            Log.Write($"store: no data file yet ({Path})");
            return;
        }

        try
        {
            var json = File.ReadAllText(Path);
            var file = JsonSerializer.Deserialize<NotesFile>(json, JsonOptions)
                       ?? throw new JsonException("root is null");
            if (file.Notes is null)
            {
                throw new JsonException("notes is null");
            }

            if (file.Schema != 0 && file.Schema != SchemaVersion)
            {
                Log.Write($"store: schema={file.Schema} (expected {SchemaVersion}), loading leniently");
            }

            var dropped = 0;
            var truncated = 0;
            foreach (var dto in file.Notes)
            {
                if (!Guid.TryParse(dto.Id, out var id))
                {
                    id = Guid.NewGuid();
                    Log.Write("store: note id missing/invalid, assigned a new one");
                }

                var text = dto.Text ?? string.Empty;
                if (text.Length > Metrics.MaxTextLength)
                {
                    text = text[..Metrics.MaxTextLength];
                    truncated++;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    dropped++;
                    continue;
                }

                var createdAt = ParseTime(dto.CreatedAt, DateTimeOffset.Now);
                var updatedAt = ParseTime(dto.UpdatedAt, createdAt);
                if (!Quadrants.TryParse(dto.Quadrant, out var quadrant))
                {
                    Log.Write($"store: unknown quadrant \"{dto.Quadrant}\", using {Quadrants.Key(Quadrants.Default)}");
                }

                _notes.Add(new NoteItem
                {
                    Id = id,
                    Text = text,
                    Quadrant = quadrant,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                });
            }

            _notes.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));

            if (_notes.Count > Metrics.MaxNotes)
            {
                var extra = _notes.Count - Metrics.MaxNotes;
                _notes.RemoveRange(0, extra);
                Log.Write($"store: over limit, dropped {extra} oldest notes");
            }

            if (dropped > 0)
            {
                Log.Write($"store: dropped {dropped} empty notes");
            }

            if (truncated > 0)
            {
                Log.Write($"store: truncated {truncated} notes to {Metrics.MaxTextLength} chars");
            }

            Log.Write($"store: loaded {_notes.Count} notes from {Path}");
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LoadError = error.Message;
            Log.Write($"store: load failed: {error.GetType().Name}: {error.Message}");
            BackupCorruptFile();
            _notes.Clear();
        }
    }

    public bool Add(NoteItem note)
    {
        if (!CanAdd)
        {
            Log.Write($"store: add rejected, limit {Metrics.MaxNotes} reached");
            return false;
        }

        note.IsDraft = false;
        note.Text = NoteItem.NormalizeText(note.Text);
        note.UpdatedAt = DateTimeOffset.Now;
        InsertSorted(note);
        ScheduleSave();
        Log.Write($"store: added {note.Id} quadrant={Quadrants.Key(note.Quadrant)} len={note.Text.Length}");
        return true;
    }

    public bool Remove(NoteItem note)
    {
        var removed = _notes.Remove(note);
        if (!removed)
        {
            return false;
        }

        ScheduleSave();
        Log.Write($"store: removed {note.Id}, {_notes.Count} left");
        return true;
    }

    public void Touch(NoteItem note)
    {
        note.Text = NoteItem.NormalizeText(note.Text);
        note.UpdatedAt = DateTimeOffset.Now;
        ScheduleSave();
        Log.Write($"store: updated {note.Id} quadrant={Quadrants.Key(note.Quadrant)} len={note.Text.Length}");
    }

    public void ScheduleSave()
    {
        if (ReadOnly)
        {
            Log.Write("store: read-only mode, save skipped");
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    public void SaveNow()
    {
        if (ReadOnly)
        {
            return;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new NotesFile
            {
                Schema = SchemaVersion,
                Notes = _notes.Select(ToDto).ToList(),
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(file, JsonOptions);
            var temp = Path + ".tmp";

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(Path))
            {
                try
                {
                    File.Replace(temp, Path, null);
                }
                catch (IOException)
                {
                    File.Move(temp, Path, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, Path);
            }

            Log.Write($"store: saved {_notes.Count} notes atomically to {Path}");
        }
        catch (Exception error)
        {
            Log.Write($"store: save failed: {error.GetType().Name}: {error.Message}");
        }
    }

    public void Flush()
    {
        _debounce.Stop();
        SaveNow();
    }

    public void Dispose()
    {
        _debounce.Stop();
        Flush();
    }

    private void InsertSorted(NoteItem note)
    {
        var index = _notes.Count;
        for (var i = 0; i < _notes.Count; i++)
        {
            if (_notes[i].CreatedAt > note.CreatedAt)
            {
                index = i;
                break;
            }
        }

        _notes.Insert(index, note);
    }

    private void BackupCorruptFile()
    {
        var directory = System.IO.Path.GetDirectoryName(Path) ?? ".";
        var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backup = System.IO.Path.Combine(directory, $"{stem}.corrupt-{stamp}.json");
        var suffix = 2;
        while (File.Exists(backup))
        {
            backup = System.IO.Path.Combine(directory, $"{stem}.corrupt-{stamp}-{suffix}.json");
            suffix++;
        }

        try
        {
            File.Move(Path, backup);
            CorruptBackupPath = backup;
            Log.Write($"store: corrupt file renamed to {backup}");
        }
        catch (Exception error)
        {
            Log.Write($"store: corrupt file rename failed: {error.GetType().Name}: {error.Message}");
            try
            {
                File.Copy(Path, backup, overwrite: false);
                CorruptBackupPath = backup;
                Log.Write($"store: corrupt file copied to {backup} (original left in place)");
            }
            catch (Exception copyError)
            {
                ReadOnly = true;
                Log.Write($"store: corrupt file backup failed, read-only: {copyError.GetType().Name}: {copyError.Message}");
            }
        }
    }

    private static DateTimeOffset ParseTime(string? value, DateTimeOffset fallback)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        Log.Write($"store: invalid timestamp \"{value}\", using {fallback:O}");
        return fallback;
    }

    private static NoteDto ToDto(NoteItem note) => new()
    {
        Id = note.Id.ToString(),
        Text = note.Text,
        Quadrant = Quadrants.Key(note.Quadrant),
        CreatedAt = note.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        UpdatedAt = note.UpdatedAt.ToString("O", CultureInfo.InvariantCulture),
    };

    private sealed class NotesFile
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; } = SchemaVersion;

        [JsonPropertyName("notes")]
        public List<NoteDto>? Notes { get; set; }
    }

    private sealed class NoteDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("quadrant")]
        public string? Quadrant { get; set; }

        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
    }
}
