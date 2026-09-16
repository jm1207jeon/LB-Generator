// 라벨 렌더러 — editor.js drawObject · _fitRect, barcode.js draw 를 SkiaSharp 로 옮긴다.
using LaPrint.Core.Model;
using LaPrint.Core.Typography;
using SkiaSharp;

namespace LaPrint.Core.Render;

/// <summary>객체 하나를 그린 결과. App 은 이 값으로 넘침·오류 장식을 덧그린다.</summary>
public sealed record RenderResult(bool Ok, string? Error = null, bool OverflowX = false, bool OverflowY = false,
                                  bool ImageMissing = false, TextLayout? Layout = null);

/// <summary>서식 전체 또는 객체 하나를 캔버스에 그린다. scale 은 px/mm.</summary>
public static class LabelRenderer
{
    public static void Render(SKCanvas c, LabelTemplate t, RenderContext ctx, double scale, double ox, double oy)
        => throw new NotImplementedException("LabelRenderer.Render — 아직 구현되지 않았습니다");

    public static RenderResult DrawObject(SKCanvas c, LabelObject o, RenderContext ctx, double scale, double ox, double oy)
        => throw new NotImplementedException("LabelRenderer.DrawObject — 아직 구현되지 않았습니다");

    /// <summary>원본 nw×nh 를 영역 안에 맞춘 사각형 (contain | cover | stretch, 가로·세로 정렬).</summary>
    public static SKRect FitRect(double nw, double nh, double X, double Y, double W, double H, string hAlign, string vAlign, string fitMode)
        => throw new NotImplementedException("LabelRenderer.FitRect — 아직 구현되지 않았습니다");

    /// <summary>흰 배경 비트맵으로 렌더 (ZPL·미리보기용).</summary>
    public static SKBitmap RenderToBitmap(LabelTemplate t, RenderContext ctx, double dpi)
        => throw new NotImplementedException("LabelRenderer.RenderToBitmap — 아직 구현되지 않았습니다");
}
