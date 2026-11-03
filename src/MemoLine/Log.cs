using System.Diagnostics;
using System.IO;

namespace MemoLine;

internal static class Log
{
    private static readonly object Gate = new();

    public static string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "memoline.log");

    public static void Reset()
    {
        Write($"=== MemoLine run started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        Write($"log file: {Path}");
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Debug.WriteLine(line);
        lock (Gate)
        {
            // 自检/辅助进程可能与主进程共用同一个日志文件，撞独占时短重试即可。
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                    writer.WriteLine(line);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(25);
                }
                catch (Exception)
                {
                    return;
                }
            }
        }
    }
}
