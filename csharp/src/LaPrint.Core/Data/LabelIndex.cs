// 품목번호 색인 — 정크 키 제외, 중복은 첫 행, 검색 가중치(품목번호 → 규격 → 제품명) (data.js buildIndex / searchProducts).
using System.Text.RegularExpressions;

namespace LaPrint.Core.Data;

/// <summary>검색 결과 한 건.</summary>
public sealed record SearchEntry(string Key, string Ref, string Name);

/// <summary>품목번호 → 행 색인과 검색.</summary>
public sealed class LabelIndex
{
    private sealed record Entry(string Key, string Lk, string Ref, string Lref, string Name, string Lname);

    private static readonly Regex ZeroRe = new(@"^0+(\.0+)?$", RegexOptions.Compiled);
    private static readonly Regex ImageRe = new(@"\.(png|jpe?g|gif|bmp|webp|svg)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExtRe = new(@"\.[A-Za-z0-9]+$", RegexOptions.Compiled);
    private static readonly Regex TrailingNoRe = new(@"[ _-]*\d+$", RegexOptions.Compiled);

    private readonly List<Entry> _search = new();

    public IReadOnlyDictionary<string, DbRow> ByRef { get; init; } = new Dictionary<string, DbRow>();
    public IReadOnlyList<(string Key, int Count)> Dups { get; init; } = Array.Empty<(string, int)>();
    public int Skipped { get; init; }
    public int Total { get; init; }

    /// <summary>품목번호 → 행 색인 + 검색용 소문자 캐시. 정크 키는 건너뛰고 중복은 먼저 나온 행을 쓴다.</summary>
    public static LabelIndex Build(IEnumerable<DbRow> rows, FieldMap map)
    {
        var kc = (string.IsNullOrWhiteSpace(map.KeyCol) ? "H" : map.KeyCol.Trim()).ToUpperInvariant();
        var byRef = new Dictionary<string, DbRow>();
        var count = new Dictionary<string, int>();
        var search = new List<Entry>();
        var skipped = 0;
        var total = 0;
        foreach (var r in rows)
        {
            total++;
            var key = r.Get(kc).Trim();
            if (IsJunkKey(key)) { skipped++; continue; }
            count[key] = count.TryGetValue(key, out var n) ? n + 1 : 1;
            if (!byRef.ContainsKey(key)) byRef[key] = r;       // 먼저 나온 행을 쓴다
            var refv = First(r.Get(map.Cols.GetValueOrDefault("REF")), r.Get(map.Cols.GetValueOrDefault("DOMESTIC")),
                             r.Get(map.Cols.GetValueOrDefault("CATALOG")));
            var name = ProductName(r, map);
            search.Add(new Entry(key, key.ToLowerInvariant(), refv, refv.ToLowerInvariant(), name, name.ToLowerInvariant()));
        }
        var dups = count.Where(kv => kv.Value > 1).Select(kv => (kv.Key, kv.Value)).ToList();
        // 안정 정렬 (JS Array.sort) — 같은 횟수는 처음 나온 순서
        dups = dups.OrderByDescending(d => d.Item2).ToList();
        var idx = new LabelIndex { ByRef = byRef, Dups = dups, Skipped = skipped, Total = total };
        idx._search.AddRange(search);
        return idx;
    }

    /// <summary>품목 검색. 품목번호 일치 → 앞부분 일치 → 포함(품목번호·규격·제품명) 순.</summary>
    public IReadOnlyList<SearchEntry> Search(string query, int limit = 60)
    {
        var q = (query ?? "").Trim().ToLowerInvariant();
        if (_search.Count == 0) return Array.Empty<SearchEntry>();
        if (q.Length == 0) return _search.Take(limit).Select(ToEntry).ToList();
        var exact = new List<Entry>();
        var prefix = new List<Entry>();
        var contains = new List<Entry>();
        foreach (var s in _search)
        {
            if (s.Lk == q) exact.Add(s);
            else if (s.Lk.StartsWith(q, StringComparison.Ordinal)) prefix.Add(s);
            else if (s.Lk.Contains(q, StringComparison.Ordinal) || s.Lref.Contains(q, StringComparison.Ordinal)
                     || s.Lname.Contains(q, StringComparison.Ordinal)) contains.Add(s);
            if (exact.Count + prefix.Count >= limit) break;
        }
        return exact.Concat(prefix).Concat(contains).Take(limit).Select(ToEntry).ToList();
    }

    private static SearchEntry ToEntry(Entry s) => new(s.Key, s.Ref, s.Name);

    /// <summary>품목번호로 쓸 수 없는 값인가 — 빈 값, '-', '.', '#N/A', 0/00/0.0 …</summary>
    public static bool IsJunkKey(string? k)
    {
        var v = (k ?? "").Trim();
        if (v.Length == 0) return true;
        if (v == "-" || v == "." || v == "#N/A") return true;
        if (ZeroRe.IsMatch(v)) return true;      // 0, 00, 0.0 …
        return false;
    }

    /// <summary>라벨DB 값이 그림 파일명처럼 보이는가 ('그림파일 없음' 같은 메모와 구분).</summary>
    public static bool LooksLikeImageName(string? v)
        => ImageRe.IsMatch((v ?? "").Trim());

    /// <summary>검색·목록에 보여 줄 제품명. 텍스트 열이 없으면 그림 파일명에서 확장자와 끝의 줄 번호를 뗀다.</summary>
    public static string ProductName(DbRow r, FieldMap map)
    {
        var direct = First(r.Get(map.Cols.GetValueOrDefault("PRODUCT")), r.Get(map.Cols.GetValueOrDefault("PRODUCT_EN"))).Trim();
        if (direct.Length > 0) return direct;
        var img = First(r.Get(map.Cols.GetValueOrDefault("IMG_NAME1")), r.Get(map.Cols.GetValueOrDefault("IMG_STENT"))).Trim();
        if (img.Length == 0 || !LooksLikeImageName(img)) return "";
        return TrailingNoRe.Replace(ExtRe.Replace(img, ""), "").Trim();
    }

    // JS 의 a || b || '' — 처음으로 비어 있지 않은 값
    private static string First(params string[] vals)
    {
        foreach (var v in vals) if (v.Length > 0) return v;
        return "";
    }
}
