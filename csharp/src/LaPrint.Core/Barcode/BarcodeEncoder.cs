// 바코드 인코딩 — ZXing.Net 으로 모듈 행렬을 만든다. 그리기는 Render 가 맡는다.
namespace LaPrint.Core.Barcode;

/// <summary>인코딩 결과. Modules 는 [y,x], 1D 는 ModulesH = 1. 실패하면 Error 에 한국어 문구.</summary>
public sealed record BarcodeSymbol(bool[,]? Modules, int ModulesW, int ModulesH, string? Error, int QuietZone);

/// <summary>심볼로지별 인코더 (gs1datamatrix · gs1-128 · datamatrix · code128 · qrcode).</summary>
public static class BarcodeEncoder
{
    public static BarcodeSymbol Encode(string symbology, string data, bool humanReadable = false)
        => throw new NotImplementedException("BarcodeEncoder.Encode — 아직 구현되지 않았습니다");

    /// <summary>ZXing 오류 메시지를 사용자용 한국어 문구로.</summary>
    public static string HumanizeError(string raw)
        => throw new NotImplementedException("BarcodeEncoder.HumanizeError — 아직 구현되지 않았습니다");
}
