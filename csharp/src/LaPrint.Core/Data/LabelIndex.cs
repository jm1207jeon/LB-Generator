// 품목번호 색인 — 정크 키 제외, 중복은 첫 행, 검색 가중치(품목번호 → 규격 → 제품명).
namespace LaPrint.Core.Data;

/// <summary>검색 결과 한 건.</summary>
public sealed record SearchEntry(string Key, string Ref, string Name);

/// <summary>품목번호 → 행 색인과 검색.</summary>
public sealed class LabelIndex
{
    public IReadOnlyDictionary<string, DbRow> ByRef { get; init; } = new Dictionary<string, DbRow>();
    public IReadOnlyList<(string Key, int Count)> Dups { get; init; } = Array.Empty<(string, int)>();
    public int Skipped { get; init; }
    public int Total { get; init; }

    public static LabelIndex Build(IEnumerable<DbRow> rows, FieldMap map)
        => throw new NotImplementedException("LabelIndex.Build — 아직 구현되지 않았습니다");

    public IReadOnlyList<SearchEntry> Search(string query, int limit = 60)
        => throw new NotImplementedException("LabelIndex.Search — 아직 구현되지 않았습니다");

    public static bool IsJunkKey(string? k)
        => throw new NotImplementedException("LabelIndex.IsJunkKey — 아직 구현되지 않았습니다");

    public static bool LooksLikeImageName(string? v)
        => throw new NotImplementedException("LabelIndex.LooksLikeImageName — 아직 구현되지 않았습니다");

    public static string ProductName(DbRow r, FieldMap map)
        => throw new NotImplementedException("LabelIndex.ProductName — 아직 구현되지 않았습니다");
}
