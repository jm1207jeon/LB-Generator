// 지원 심볼로지 목록 — barcode.js SYMBOLOGIES 와 같은 순서·문구.
namespace LaPrint.Core.Barcode;

/// <summary>심볼로지 정보. Is2D 는 2차원, Gs1 은 GS1 AI 검증 대상.</summary>
public sealed record SymbologyInfo(string Id, string Name, bool Is2D, bool Gs1, string Note);

/// <summary>지원 심볼로지. 모르는 id 는 첫 번째(GS1 DataMatrix)로 본다.</summary>
public static class Symbologies
{
    public static IReadOnlyList<SymbologyInfo> All { get; } = new[]
    {
        new SymbologyInfo("gs1datamatrix", "GS1 DataMatrix", true, true, "UDI 표준 (의료기기)"),
        new SymbologyInfo("gs1-128", "GS1-128", false, true, "물류/포장 1D"),
        new SymbologyInfo("datamatrix", "DataMatrix (일반)", true, false, "자유 텍스트"),
        new SymbologyInfo("code128", "Code 128", false, false, "자유 텍스트 1D"),
        new SymbologyInfo("qrcode", "QR Code", true, false, "자유 텍스트 2D"),
    };

    public static SymbologyInfo ById(string? id)
    {
        foreach (var s in All)
            if (s.Id == id) return s;
        return All[0];
    }
}
