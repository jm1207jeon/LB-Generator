// 바코드 데이터 바인딩 — field | expression | object(다른 객체 텍스트 링크, 순환 참조 차단).
using LaPrint.Core.Data;
using LaPrint.Core.Model;

namespace LaPrint.Core.Barcode;

/// <summary>바인딩 해석에 필요한 문맥.</summary>
public sealed record BindingContext(Fields Fields, IReadOnlyList<LabelObject> Objects, Func<string, string> ResolveText);

/// <summary>바코드 객체의 데이터 문자열을 구하고 물리 조건을 검사한다.</summary>
public static class BarcodeBinding
{
    public static string ResolveData(BarcodeObject o, BindingContext ctx, HashSet<string>? seen = null)
        => throw new NotImplementedException("BarcodeBinding.ResolveData — 아직 구현되지 않았습니다");

    /// <summary>링크할 수 있는 객체 목록 (자기 자신 제외).</summary>
    public static IReadOnlyList<(string Id, string Label)> LinkableObjects(IEnumerable<LabelObject> objs, string selfId)
        => throw new NotImplementedException("BarcodeBinding.LinkableObjects — 아직 구현되지 않았습니다");

    /// <summary>X-dim ≥ 0.254mm, GS1-128 높이 ≥ max(6.35, 0.15W), 정수 도트 검사.</summary>
    public static IReadOnlyList<Issue> CheckPhysical(BarcodeObject o, string data, double dpi)
        => throw new NotImplementedException("BarcodeBinding.CheckPhysical — 아직 구현되지 않았습니다");
}
