// 플레이스홀더 치환 — {FIELD} {FIELD:fmt} {@AB} {FIELD|대체} (data.js resolveText).
using System.Text.RegularExpressions;

namespace LaPrint.Core.Data;

/// <summary>텍스트 안의 플레이스홀더를 필드 값으로 바꾼다.</summary>
public static class Placeholders
{
    private static readonly Regex TokenRe =
        new(@"\{(@?)([A-Za-z0-9_가-힣]+)(?::([^}|]+))?(?:\|([^}]*))?\}", RegexOptions.Compiled);
    private static readonly Regex LeftRe =
        new(@"\{@?[A-Za-z0-9_가-힣]+(?::[^}|]+)?(?:\|[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>
    /// {FIELD} 필드 값 · {FIELD:YYYY-MM-DD} 날짜 형식(MFG/EXP/TODAY) · {@AJ} 라벨DB 열 직접 참조 · {FIELD|대체값} 빈 값 대체.
    /// 값이 비면 대체값 → 필드가 있으면 "" → 모르는 키는 토큰 그대로.
    /// </summary>
    public static string Resolve(string text, Fields f, DbRow? row)
    {
        if (text is null) return "";
        return TokenRe.Replace(text, m =>
        {
            var at = m.Groups[1].Value.Length > 0;
            var key = m.Groups[2].Value;
            var fmt = m.Groups[3].Success ? m.Groups[3].Value : null;
            string? v;
            if (at)
            {
                var col = key.ToUpperInvariant();
                v = row is null ? "" : row.Get(col);
            }
            else if (fmt is not null && (key == "MFG" || key == "EXP" || key == "TODAY"))
            {
                DateTime? d = key == "TODAY" ? DateTime.Now : key == "MFG" ? f?.MfgDate : f?.ExpDate;
                v = FieldComputer.FormatDate(d, fmt);
            }
            else
            {
                v = f is not null && f.TryGetValue(key, out var fv) ? fv : null;
            }
            if (string.IsNullOrEmpty(v))
            {
                if (m.Groups[4].Success) return m.Groups[4].Value;
                return f is not null && f.ContainsKey(key) ? "" : m.Value;
            }
            return v;
        });
    }

    /// <summary>치환되지 않고 남은 플레이스홀더 (검증용).</summary>
    public static IReadOnlyList<string> Unresolved(string text, Fields f, DbRow? row)
    {
        var resolved = Resolve(text, f, row);
        return LeftRe.Matches(resolved).Select(m => m.Value).ToList();
    }

    /// <summary>플레이스홀더 자동완성 목록 (키, 토큰, 한국어 이름). 중복 없이 고정 순서.</summary>
    public static IReadOnlyList<(string Key, string Token, string Label)> List(FieldMap map)
    {
        var outList = new List<(string, string, string)>();
        var seen = new HashSet<string>();
        void Push(string k)
        {
            if (!seen.Add(k)) return;
            outList.Add((k, "{" + k + "}", FieldMap.Labels.TryGetValue(k, out var l) ? l : k));
        }
        foreach (var k in new[] { "ITEM", "LOT", "SN", "MFG", "EXP", "EXP6", "MFG6" }) Push(k);
        foreach (var k in new[] { "UDI_FULL", "UDI_L1", "UDI_L2", "GTIN01", "GTIN" }) Push(k);
        foreach (var k in map.Cols.Keys) Push(k);
        foreach (var k in new[] { "TODAY", "NOW", "DATE", "TIME" }) Push(k);
        return outList;
    }
}
