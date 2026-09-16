// 앱 데이터 경로 — %APPDATA%\LaPrint 아래 설정·서식·세션·이력·캐시.
namespace LaPrint.Core.Storage;

/// <summary>앱 데이터 파일 위치.</summary>
public static class AppPaths
{
    /// <summary>%APPDATA%\LaPrint.</summary>
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaPrint");

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
    public static string TemplatesDir { get; } = Path.Combine(Root, "templates");
    public static string SessionFile { get; } = Path.Combine(Root, "session.json");
    public static string HistoryFile { get; } = Path.Combine(Root, "history.jsonl");
    public static string CacheDir { get; } = Path.Combine(Root, "cache");
}
