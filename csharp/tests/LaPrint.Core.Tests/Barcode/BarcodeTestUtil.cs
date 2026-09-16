// 바코드 테스트 보조 — 모듈 행렬을 비트맵으로 그리고 ZXing.Net 으로 다시 읽는다.
using LaPrint.Core.Barcode;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using Xunit;

namespace LaPrint.Core.Tests.Barcode;

/// <summary>디코드 결과 — 문자열과 심볼로지 식별자.</summary>
public sealed record Decoded(string Text, string? SymbologyId, BarcodeFormat Format);

/// <summary>모듈 → 비트맵 → 디코드 왕복 도우미.</summary>
public static class BarcodeTestUtil
{
    /// <summary>1D 막대를 몇 모듈 높이로 그릴지.</summary>
    public const int BarHeightModules = 40;

    /// <summary>모듈 행렬을 px/module 배율과 흰 여백(모듈 단위)으로 그린다.</summary>
    public static SKBitmap RenderBitmap(BarcodeSymbol s, int pxPerModule = 6, int marginModules = 12)
    {
        Assert.NotNull(s.Modules);
        var h = s.ModulesH == 1 ? BarHeightModules : s.ModulesH;
        var bmp = new SKBitmap((s.ModulesW + 2 * marginModules) * pxPerModule, (h + 2 * marginModules) * pxPerModule);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        using var p = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var y = 0; y < h; y++)
            for (var x = 0; x < s.ModulesW; x++)
                if (s.Modules![s.ModulesH == 1 ? 0 : y, x])
                    c.DrawRect((x + marginModules) * pxPerModule, (y + marginModules) * pxPerModule, pxPerModule, pxPerModule, p);
        return bmp;
    }

    /// <summary>ZXing.Net(SkiaSharp 바인딩)으로 읽는다. assumeGs1 이면 FNC1 구분자가 GS 문자로 나온다.</summary>
    public static Decoded? Decode(SKBitmap bmp, bool assumeGs1 = false)
    {
        var reader = new ZXing.SkiaSharp.BarcodeReader
        {
            AutoRotate = false,
            Options = new DecodingOptions { TryHarder = true, PureBarcode = false, AssumeGS1 = assumeGs1 },
        };
        var r = reader.Decode(bmp);
        if (r is null) return null;
        string? sid = null;
        if (r.ResultMetadata is not null && r.ResultMetadata.TryGetValue(ResultMetadataType.SYMBOLOGY_IDENTIFIER, out var v))
            sid = v?.ToString();
        return new Decoded(r.Text, sid, r.BarcodeFormat);
    }

    /// <summary>바이너리 픽스처 PNG 를 읽는다.</summary>
    public static SKBitmap LoadPng(string sym)
    {
        var path = Fixtures.Path(System.IO.Path.Combine("barcodes", sym + ".png"));
        var bmp = SKBitmap.Decode(path);
        Assert.NotNull(bmp);
        return bmp;
    }

    /// <summary>ZXing.Net 이 GS1 디코드 결과 앞에 붙이는 표식(]C1 · 선두 FNC1 의 GS)을 벗긴다.</summary>
    public static string StripGs1Prefix(string text, string sym)
    {
        if (sym == "gs1-128" && text.StartsWith("]C1", StringComparison.Ordinal)) return text[3..];
        if (sym == "gs1datamatrix" && text.Length > 0 && text[0] == Gs1.GS) return text[1..];
        return text;
    }

    /// <summary>GS 문자를 눈에 보이게 바꿔 실패 메시지에 쓴다.</summary>
    public static string Show(string s) => s.Replace(Gs1.GS.ToString(), "<GS>");
}
