// 앱 데이터 경로 — %APPDATA%\LaPrint 아래 설정·서식·세션·이력·캐시·로그.
namespace LaPrint.Core.Storage;

/// <summary>앱 데이터 파일 위치. 테스트에서는 Override 로 뿌리를 바꾼다.</summary>
public static class AppPaths
{
    private static string _root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaPrint");

    /// <summary>%APPDATA%\LaPrint.</summary>
    public static string Root => _root;

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string TemplatesDir => Path.Combine(Root, "templates");
    public static string SessionFile => Path.Combine(Root, "session.json");
    public static string HistoryFile => Path.Combine(Root, "history.jsonl");
    public static string CacheDir => Path.Combine(Root, "cache");
    public static string LogFile => Path.Combine(Root, "app.log");
    public static string PrevLogFile => Path.Combine(Root, "app.prev.log");

    /// <summary>뿌리 폴더를 바꾼다 (테스트·이동식 설치용).</summary>
    public static void Override(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    /// <summary>뿌리 폴더가 없으면 만든다.</summary>
    public static string EnsureRoot()
    {
        Directory.CreateDirectory(Root);
        return Root;
    }
}
