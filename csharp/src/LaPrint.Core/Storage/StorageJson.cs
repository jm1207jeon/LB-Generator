// 저장소 공용 JSON 설정과 원자적 파일 쓰기 — 서식 JSON 과 같은 camelCase 규칙.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaPrint.Core.Storage;

/// <summary>저장소 파일들의 직렬화 규칙과 tmp → Move 원자 저장.</summary>
public static class StorageJson
{
    /// <summary>들여쓰기된 camelCase (settings.json · session.json).</summary>
    public static JsonSerializerOptions Indented { get; } = Make(true);

    /// <summary>한 줄 camelCase (history.jsonl · 캐시).</summary>
    public static JsonSerializerOptions Compact { get; } = Make(false);

    private static JsonSerializerOptions Make(bool indented) => new()
    {
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>임시 파일에 쓴 뒤 교체한다 — 쓰는 도중 꺼져도 이전 파일이 남는다.</summary>
    public static void WriteAtomic(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>임시 파일에 바이트를 쓴 뒤 교체한다.</summary>
    public static void WriteAtomic(string path, Action<Stream> write)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            write(fs);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>파일 이름으로 쓸 수 없는 문자를 '_' 로 바꾼다 (한글은 그대로).</summary>
    public static string SafeFileName(string name)
    {
        var s = (name ?? "").Trim();
        if (s.Length == 0) return "_";
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(Array.IndexOf(bad, ch) >= 0 || ch < ' ' ? '_' : ch);
        var r = sb.ToString().TrimEnd('.', ' ');
        return r.Length == 0 ? "_" : (r.Length > 120 ? r[..120] : r);
    }
}
