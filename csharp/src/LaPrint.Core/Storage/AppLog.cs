// 앱 로그 — %APPDATA%\LaPrint\app.log 에 한 줄씩. 1 MB 를 넘으면 app.prev.log 로 돌린다. 자기 오류는 삼킨다.
using System.Globalization;
using System.Text;

namespace LaPrint.Core.Storage;

/// <summary>간단한 파일 로그. 로깅 실패가 앱을 멈추지 않도록 모든 예외를 삼킨다.</summary>
public static class AppLog
{
    /// <summary>회전 기준 크기 (1 MB).</summary>
    public const long RotateBytes = 1024 * 1024;

    private static readonly object Gate = new();

    public static void Info(string msg) => Write("INFO", msg, null);
    public static void Warn(string msg, Exception? ex = null) => Write("WARN", msg, ex);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", msg, ex);

    private static void Write(string level, string msg, Exception? ex)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] ")
                .Append(msg ?? "");
            if (ex is not null) line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            line.Append(Environment.NewLine);

            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var path = AppPaths.LogFile;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length >= RotateBytes)
                    File.Move(path, AppPaths.PrevLogFile, overwrite: true);
                File.AppendAllText(path, line.ToString(), new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
            // 로그 실패는 무시한다
        }
    }
}
