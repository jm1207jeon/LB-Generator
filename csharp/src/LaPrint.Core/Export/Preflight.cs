// 출력 전 점검 — 데이터 검증 + 서식 검증(넘침·영역 밖·미해결 플레이스홀더·바코드) 을 한 목록으로.
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using LaPrint.Core.Render;

namespace LaPrint.Core.Export;

/// <summary>점검 결과. All 을 수준별로 나눠 둔다.</summary>
public sealed record PreflightResult(IReadOnlyList<Issue> All, IReadOnlyList<Issue> Errors, IReadOnlyList<Issue> Warnings, IReadOnlyList<Issue> Infos);

/// <summary>출력 전 점검 (exporter.js preflight).</summary>
public static class Preflight
{
    public static PreflightResult Run(LabelTemplate t, RenderContext ctx, ValidationRules rules, double dpi)
        => throw new NotImplementedException("Preflight.Run — 아직 구현되지 않았습니다");
}
