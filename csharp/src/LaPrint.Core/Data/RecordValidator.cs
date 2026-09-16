// 작업 1건(필드+행) 검증 — 필수 입력·GTIN·유효기간 순서 등 (data.js validateRecord).
namespace LaPrint.Core.Data;

/// <summary>필드 값 검증. 출력 전 점검의 데이터 부분.</summary>
public static class RecordValidator
{
    public static IReadOnlyList<Issue> Validate(Fields f, DbRow? row, ValidationRules r)
        => throw new NotImplementedException("RecordValidator.Validate — 아직 구현되지 않았습니다");

    /// <summary>LOT 형식이 평소와 다를 때의 힌트 문구. 없으면 null.</summary>
    public static string? LotHint(string lot)
        => throw new NotImplementedException("RecordValidator.LotHint — 아직 구현되지 않았습니다");
}
