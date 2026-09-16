// 앱 로그 — %APPDATA%\LaPrint\app.log, 1 MB 회전, "yyyy-MM-dd HH:mm:ss.fff [LEVEL] msg" (UDInspect 와 같은 형식).
using System.IO;
using LaPrint.Core.Storage;

namespace LaPrint.App.Services;

/// <summary>파일 로그. 실패해도 앱을 멈추지 않는다.</summary>
public static class AppLog
{
    private static readonly object Lock = new();
    private static readonly string Dir = AppPaths.Root;
    private const long MaxBytes = 1024 * 1024;

    /// <summary>로그 파일 경로 (오류 대화상자에서 안내).</summary>
    public static readonly string FilePath = Path.Combine(Dir, "app.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(Dir, "app.prev.log"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
