// 라벨 렌더러 — editor.js drawObject · _fitRect · getImage, barcode.js draw, exporter.js renderToCanvas · effectiveDpi 를 SkiaSharp 로 옮긴다.
//
// 그리는 순서: (출력이면 흰 바탕) → 배경 서식을 라벨 영역에 늘려 그림 → 객체를 배열 순서대로. 출력은 라벨 영역으로 클립.
// 편집 장식(선택 테두리·고스트·격자)은 App 이 덧그린다 — 여기서는 ForExport 가 false 여도 그리지 않는다.
using LaPrint.Core.Barcode;
using LaPrint.Core.Model;
using LaPrint.Core.Typography;
using SkiaSharp;

namespace LaPrint.Core.Render;

/// <summary>객체 하나를 그린 결과. App 은 이 값으로 넘침·오류 장식을 덧그린다. 바코드는 그린 크기·모듈 px·품질을 함께 준다.</summary>
public sealed record RenderResult(bool Ok, string? Error = null, bool OverflowX = false, bool OverflowY = false,
                                  bool ImageMissing = false, TextLayout? Layout = null,
                                  double DrawnW = 0, double DrawnH = 0, double ModulePx = 0, string Quality = "");

/// <summary>서식 전체 또는 객체 하나를 캔버스에 그린다. scale 은 px/mm.</summary>
public static class LabelRenderer
{
    /// <summary>래스터 상한 픽셀 수 — 넘으면 DPI 를 낮춘다 (exporter.js MAX_PIXELS).</summary>
    public const double MaxPixels = 42e6;

    private const int ImageCacheLimit = 120;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SKBitmap?> ImageCache = new();
    private static readonly LinkedList<string> ImageOrder = new();

    /// <summary>서식 한 장을 그린다. 객체는 ctx.Objects(비어 있으면 t.Objects)를 배열 순서대로.</summary>
    public static void Render(SKCanvas c, LabelTemplate t, RenderContext ctx, double scale, double ox, double oy)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(ctx);

        var L = t.Label ?? new LabelSize();
        var LW = (float)(L.W * scale);
        var LH = (float)(L.H * scale);
        var rect = new SKRect((float)ox, (float)oy, (float)ox + LW, (float)oy + LH);

        c.Save();
        if (ctx.ForExport)
        {
            c.ClipRect(rect);
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            c.DrawRect(rect, white);
        }

        // 배경 서식 — 라벨 영역에 늘려 그린다
        if (ctx.IncludeBg)
        {
            var bg = ctx.Background ?? GetImage(L.Bg);
            if (bg is not null)
            {
                c.Save();
                c.ClipRect(rect);
                using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
                c.DrawBitmap(bg, rect, p);
                c.Restore();
            }
        }

        var objs = ctx.Objects is { Count: > 0 } ? ctx.Objects : t.Objects;
        foreach (var o in objs)
        {
            if (o is null || !o.Visible) continue;
            DrawObject(c, o, ctx, scale, ox, oy);
        }
        c.Restore();
    }

    /// <summary>객체 하나 그리기 (editor.js drawObject). 편집 장식은 그리지 않는다.</summary>
    public static RenderResult DrawObject(SKCanvas c, LabelObject o, RenderContext ctx, double scale, double ox, double oy)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(ctx);

        var X = (float)(o.X * scale + ox);
        var Y = (float)(o.Y * scale + oy);
        var W = (float)(o.W * scale);
        var H = (float)(o.H * scale);

        switch (o)
        {
            case TextObject t:
            {
                var resolve = ctx.ResolveText ?? (s => s);
                var txt = resolve(t.Text ?? "");
                c.Save();
                if (t.Clip) c.ClipRect(new SKRect(X - 1, Y - 1, X + W + 1, Y + H + 1));
                var L = TextLayoutEngine.Draw(c, t, txt, scale, ox, oy);
                c.Restore();
                return new RenderResult(true, null, L.OverflowX, L.OverflowY, false, L);
            }
            case ImageObject im:
            {
                var bmp = (ctx.ImageOf is not null ? ctx.ImageOf(im.Id) : null) ?? im.Bitmap;
                if (bmp is not null)
                {
                    var r = FitRect(bmp.Width, bmp.Height, X, Y, W, H,
                        string.IsNullOrEmpty(im.Fit) ? "center" : im.Fit,
                        string.IsNullOrEmpty(im.VFit) ? "middle" : im.VFit, im.FitMode);
                    c.Save();
                    c.ClipRect(new SKRect(X, Y, X + W, Y + H));
                    using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
                    c.DrawBitmap(bmp, r, p);
                    c.Restore();
                    return new RenderResult(true, null, DrawnW: r.Width, DrawnH: r.Height);
                }
                var missing = !string.IsNullOrEmpty(im.SourceField);
                return new RenderResult(false, string.IsNullOrEmpty(im.Error) ? null : im.Error, ImageMissing: missing);
            }
            case BarcodeObject b:
            {
                var resolve = ctx.ResolveText ?? (s => s);
                var data = BarcodeBinding.ResolveData(b, new BindingContext(ctx.Fields ?? new(), ctx.Objects ?? Array.Empty<LabelObject>(), resolve));
                return DrawBarcode(c, b, data, scale, ox, oy);
            }
            default:
                return new RenderResult(false, $"알 수 없는 객체 종류입니다: \"{o.Type}\"");
        }
    }

    /// <summary>원본 nw×nh 를 영역 안에 맞춘 사각형 (contain | cover | stretch, 가로·세로 정렬). editor.js _fitRect.</summary>
    public static SKRect FitRect(double nw, double nh, double X, double Y, double W, double H, string hAlign, string vAlign, string fitMode)
    {
        if (fitMode == "stretch") return new SKRect((float)X, (float)Y, (float)(X + W), (float)(Y + H));
        if (!(nw > 0)) nw = 1;
        if (!(nh > 0)) nh = 1;
        var sc = fitMode == "cover" ? Math.Max(W / nw, H / nh) : Math.Min(W / nw, H / nh);
        var w = nw * sc;
        var h = nh * sc;
        var x = X + (W - w) / 2;
        if (hAlign == "left") x = X;
        else if (hAlign == "right") x = X + W - w;
        var y = Y + (H - h) / 2;
        if (vAlign == "top") y = Y;
        else if (vAlign == "bottom") y = Y + H - h;
        return new SKRect((float)x, (float)y, (float)(x + w), (float)(y + h));
    }

    /// <summary>바코드를 그린다 (barcode.js draw). module 모드는 모듈이 정수 px 가 되도록, stretch 는 영역을 채운다. 안티앨리어싱 끔.</summary>
    public static RenderResult DrawBarcode(SKCanvas c, BarcodeObject o, string data, double scale, double ox, double oy)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(o);

        var X = o.X * scale + ox;
        var Y = o.Y * scale + oy;
        var W = o.W * scale;
        var H = o.H * scale;
        var sym = Symbologies.ById(o.Symbology);
        var hri = o.HumanReadable;
        var fitMode = string.IsNullOrEmpty(o.FitMode) ? "module" : o.FitMode;

        if (string.IsNullOrWhiteSpace(data)) return new RenderResult(false, "데이터 없음");

        var g = BarcodeEncoder.Encode(sym.Id, data, hri);
        if (g.Modules is null || g.Error is not null) return new RenderResult(false, g.Error ?? "바코드를 만들 수 없습니다.");

        // 조용한 영역(2D 1모듈 · 1D 10모듈)을 그리는 영역 안에 포함한다
        var qz = Math.Max(0, g.QuietZone);
        var totalW = g.ModulesW + 2 * qz;
        var totalH = sym.Is2D ? g.ModulesH + 2 * qz : 1;

        // 1D + HRI: 아래쪽에 문자 띠를 두고 막대는 남은 높이를 쓴다
        var textSize = hri && !sym.Is2D ? Math.Min(H * 0.2, 3 * scale) : 0;
        var textBand = textSize > 0 ? textSize * 1.25 : 0;

        double dw, dh, modW, modH;
        if (fitMode == "stretch")
        {
            dw = W; dh = H;
            modW = dw / totalW;
            modH = sym.Is2D ? dh / totalH : Math.Max(1, dh - textBand);
        }
        else if (sym.Is2D)
        {
            // 2D: 정사각 비율 유지 + 정수 모듈
            var modPx = Math.Max(1, Math.Floor(Math.Min(W / totalW, H / totalH)));
            dw = Math.Min(totalW * modPx, W);
            dh = Math.Min(totalH * modPx, H);
            modW = dw / totalW;
            modH = dh / totalH;
        }
        else
        {
            // 1D: 가로는 정수 모듈, 세로는 영역을 채움 (막대는 세로로 균일해 늘려도 무해)
            var modPx = Math.Max(1, Math.Floor(W / totalW));
            dw = Math.Min(totalW * modPx, W);
            dh = H;
            modW = dw / totalW;
            modH = Math.Max(1, dh - textBand);
        }

        // 영역 내 정렬
        var fit = string.IsNullOrEmpty(o.Fit) ? "center" : o.Fit;
        var dx = X + (W - dw) / 2;
        if (fit == "left") dx = X;
        else if (fit == "right") dx = X + W - dw;
        var vfit = string.IsNullOrEmpty(o.VFit) ? "middle" : o.VFit;
        var dy = Y + (H - dh) / 2;
        if (vfit == "top") dy = Y;
        else if (vfit == "bottom") dy = Y + H - dh;

        c.Save();
        if (o.Rotation != 0)
        {
            c.Translate((float)(dx + dw / 2), (float)(dy + dh / 2));
            c.RotateDegrees((float)o.Rotation);
            c.Translate((float)(-dw / 2), (float)(-dh / 2));
            DrawModules(c, g, sym.Is2D, 0, 0, dw, dh, qz, modW, modH, data, textSize, textBand);
        }
        else
        {
            DrawModules(c, g, sym.Is2D, dx, dy, dw, dh, qz, modW, modH, data, textSize, textBand);
        }
        c.Restore();

        // 품질 판정: 1모듈이 화면/출력에서 몇 px 인가
        var modulePx = modW;
        var quality = "ok";
        if (fitMode == "stretch") quality = "stretched";
        else if (modulePx < 2) quality = "low";
        return new RenderResult(true, null, DrawnW: dw, DrawnH: dh, ModulePx: modulePx, Quality: quality);
    }

    /* 흰 바탕(조용한 영역 포함) 위에 검정 모듈을 사각형으로 그린다. 1D 는 막대 아래에 HRI 문자. */
    private static void DrawModules(SKCanvas c, BarcodeSymbol g, bool is2D, double bx, double by, double dw, double dh,
                                    int qz, double modW, double modH, string data, double textSize, double textBand)
    {
        using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = false };
        using var black = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = false };
        c.DrawRect(new SKRect((float)bx, (float)by, (float)(bx + dw), (float)(by + dh)), white);

        var m = g.Modules!;
        var x0 = bx + qz * modW;
        if (is2D)
        {
            var y0 = by + qz * modH;
            for (var y = 0; y < g.ModulesH; y++)
            {
                var top = (float)(y0 + y * modH);
                var bottom = (float)(y0 + (y + 1) * modH);
                for (var x = 0; x < g.ModulesW; x++)
                {
                    if (!m[y, x]) continue;
                    // 이웃한 검정 모듈은 한 사각형으로 합쳐 경계 틈을 없앤다
                    var run = 1;
                    while (x + run < g.ModulesW && m[y, x + run]) run++;
                    c.DrawRect(new SKRect((float)(x0 + x * modW), top, (float)(x0 + (x + run) * modW), bottom), black);
                    x += run - 1;
                }
            }
        }
        else
        {
            var top = (float)by;
            var bottom = (float)(by + modH);
            for (var x = 0; x < g.ModulesW; x++)
            {
                if (!m[0, x]) continue;
                var run = 1;
                while (x + run < g.ModulesW && m[0, x + run]) run++;
                c.DrawRect(new SKRect((float)(x0 + x * modW), top, (float)(x0 + (x + run) * modW), bottom), black);
                x += run - 1;
            }
            if (textSize > 0)
            {
                using var tp = new SKPaint
                {
                    Typeface = FontProvider.Get("Arial", false, false),
                    TextSize = (float)textSize,
                    TextAlign = SKTextAlign.Center,
                    IsAntialias = true,
                    Color = SKColors.Black,
                };
                var metrics = tp.FontMetrics;
                var asc = metrics.Ascent < 0 ? -metrics.Ascent : (float)(textSize * 0.8);
                var baseline = (float)(by + modH + (textBand - textSize) / 2 + asc);
                c.DrawText(data, (float)(bx + dw / 2), baseline, tp);
            }
        }
    }

    /// <summary>흰 배경 비트맵으로 렌더 (ZPL·미리보기용). 42M 픽셀을 넘으면 DPI 를 낮춘다.</summary>
    public static SKBitmap RenderToBitmap(LabelTemplate t, RenderContext ctx, double dpi)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(ctx);
        var L = t.Label ?? new LabelSize();
        var (eff, _) = EffectiveDpi(L, dpi);
        var pxPerMm = eff / 25.4;
        var w = Math.Max(1, (int)Math.Round(L.W * pxPerMm));
        var h = Math.Max(1, (int)Math.Round(L.H * pxPerMm));
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        var prevExport = ctx.ForExport;
        var prevDpi = ctx.Dpi;
        try
        {
            ctx.ForExport = true;
            ctx.Dpi = eff;
            c.Save();
            c.ClipRect(new SKRect(0, 0, w, h));
            Render(c, t, ctx, pxPerMm, 0, 0);
            c.Restore();
        }
        finally
        {
            ctx.ForExport = prevExport;
            ctx.Dpi = prevDpi;
        }
        c.Flush();
        return bmp;
    }

    /// <summary>큰 라벨을 높은 DPI 로 만들면 수천만 픽셀이 된다. 상한을 넘으면 DPI 를 낮춰 돌려준다 (exporter.js effectiveDpi).</summary>
    public static (double Dpi, bool Reduced) EffectiveDpi(LabelSize label, double dpi)
    {
        ArgumentNullException.ThrowIfNull(label);
        var px = (label.W / 25.4 * dpi) * (label.H / 25.4 * dpi);
        if (px <= MaxPixels) return (dpi, false);
        var scale = Math.Sqrt(MaxPixels / px);
        var capped = Math.Max(150, Math.Floor(dpi * scale / 25) * 25);
        return (capped, true);
    }

    /// <summary>배경 서식 비트맵 — "res:template_bg"(내장 A3 원판) · 파일 경로 · data URL. 디코드 결과는 캐시(120)한다.</summary>
    public static SKBitmap? GetImage(string? src)
    {
        if (string.IsNullOrEmpty(src)) return null;
        var key = src;
        if (!src.StartsWith("res:", StringComparison.Ordinal) && !src.StartsWith("data:", StringComparison.Ordinal))
        {
            // 파일이 바뀌면 다시 읽도록 수정 시각을 키에 넣는다
            try { if (File.Exists(src)) key = src + "|" + File.GetLastWriteTimeUtc(src).Ticks; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        lock (Gate)
        {
            if (ImageCache.TryGetValue(key, out var hit)) return hit;
            var bmp = Decode(src);
            ImageCache[key] = bmp;
            ImageOrder.AddLast(key);
            while (ImageOrder.Count > ImageCacheLimit)
            {
                var k = ImageOrder.First!.Value;
                ImageOrder.RemoveFirst();
                ImageCache.Remove(k);
            }
            return bmp;
        }
    }

    /// <summary>배경 캐시를 비운다 (서식 배경을 바꾼 뒤).</summary>
    public static void InvalidateImages()
    {
        lock (Gate)
        {
            ImageCache.Clear();
            ImageOrder.Clear();
        }
    }

    private static SKBitmap? Decode(string src)
    {
        try
        {
            if (src == LabelSize.TemplateBg) return SKBitmap.Decode(TemplateJson.LoadBackgroundPng());
            if (src.StartsWith("res:", StringComparison.Ordinal)) return null;
            if (src.StartsWith("data:", StringComparison.Ordinal))
            {
                var comma = src.IndexOf(',');
                if (comma < 0) return null;
                var head = src[..comma];
                var body = src[(comma + 1)..];
                if (!head.Contains(";base64", StringComparison.OrdinalIgnoreCase)) return null;
                return SKBitmap.Decode(Convert.FromBase64String(body));
            }
            return File.Exists(src) ? SKBitmap.Decode(src) : null;
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
