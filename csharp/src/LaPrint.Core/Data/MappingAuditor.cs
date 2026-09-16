// 열 매칭 점검 — 표본 행에서 채움 비율·그림 파일명 비율을 세어 매칭이 의심스러운 필드를 찾는다.
namespace LaPrint.Core.Data;

/// <summary>필드 하나의 매칭 점검 결과.</summary>
public sealed record MappingAudit(string Key, string Label, string Group, string Col, int Filled, int Total, int Pct,
                                  int ImagePct, string Sample, string Level, string Note);

/// <summary>데이터 매칭 편집기의 점검표를 만든다.</summary>
public static class MappingAuditor
{
    public static IReadOnlyList<MappingAudit> Audit(IReadOnlyList<DbRow> rows, FieldMap map, int sample = 400)
        => throw new NotImplementedException("MappingAuditor.Audit — 아직 구현되지 않았습니다");
}
