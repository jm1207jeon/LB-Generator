// 검증 결과 한 건과 검증 규칙 — 출력 전 점검·큐 검증·바코드 검증이 공유한다.
namespace LaPrint.Core.Data;

/// <summary>검증 결과 한 건. Level 은 error | warn | info.</summary>
public sealed record Issue(string Level, string Code, string Msg, string? Field = null, string? ObjId = null);

/// <summary>검증 규칙 — 설정(validation)과 같은 구조.</summary>
public sealed class ValidationRules
{
    public bool RequireItem { get; set; } = true;
    public bool RequireLot { get; set; } = true;
    public bool RequireMfg { get; set; } = true;
    public bool RequireSn { get; set; } = false;
    public bool CheckGtin { get; set; } = true;
    public bool CheckExpPast { get; set; } = true;
    public bool CheckExpOrder { get; set; } = true;
    public bool WarnMissingImage { get; set; } = true;
    public bool WarnUnresolved { get; set; } = true;
    public bool WarnOverflow { get; set; } = true;
    public bool WarnOutOfBounds { get; set; } = true;
}
