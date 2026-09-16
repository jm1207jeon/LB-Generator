// 플레이스홀더 치환 — {FIELD} {FIELD:fmt} {@AB} {FIELD|대체} (data.js resolveText).
namespace LaPrint.Core.Data;

/// <summary>텍스트 안의 플레이스홀더를 필드 값으로 바꾼다.</summary>
public static class Placeholders
{
    public static string Resolve(string text, Fields f, DbRow? row)
        => throw new NotImplementedException("Placeholders.Resolve — 아직 구현되지 않았습니다");

    public static IReadOnlyList<string> Unresolved(string text, Fields f, DbRow? row)
        => throw new NotImplementedException("Placeholders.Unresolved — 아직 구현되지 않았습니다");

    public static IReadOnlyList<(string Key, string Token, string Label)> List(FieldMap map)
        => throw new NotImplementedException("Placeholders.List — 아직 구현되지 않았습니다");
}
