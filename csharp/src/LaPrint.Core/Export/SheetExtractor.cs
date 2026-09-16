// 원판에서 떼어내기 — A3 세트 서식에서 라벨 하나의 영역만 잘라 새 서식을 만든다 (extract.js).
//
// 잘라내는 것
//   1) 영역 안에 들어가는 객체 — 좌표를 라벨 원점 기준으로 옮긴다 (0.01mm 반올림)
//   2) 배경 서식 이미지 — 해당 영역만 잘라 새 이미지(data URL)로 만든다
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using SkiaSharp;

namespace LaPrint.Core.Export;

/// <summary>원판 서식에서 영역 안의 객체와 배경을 떼어낸다.</summary>
public static class SheetExtractor
{
    /// <summary>객체가 영역 안에 완전히 들어가는지 (tol mm 허용).</summary>
    public static bool Inside(LabelObject o, SKRect r, double tol = 0.8)
    {
        ArgumentNullException.ThrowIfNull(o);
        var (rx, ry, rw, rh) = RectMm(r);
        return o.X >= rx - tol && o.Y >= ry - tol &&
               o.X + o.W <= rx + rw + tol && o.Y + o.H <= ry + rh + tol;
    }

    /// <summary>객체가 영역과 일부라도 겹치는지.</summary>
    public static bool Overlaps(LabelObject o, SKRect r)
    {
        ArgumentNullException.ThrowIfNull(o);
        var (rx, ry, rw, rh) = RectMm(r);
        return o.X < rx + rw && o.X + o.W > rx && o.Y < ry + rh && o.Y + o.H > ry;
    }

    /// <summary>
    /// 원판 서식에서 라벨 한 장을 추출한다. 배경은 영역만큼 잘라 data URL(PNG) 로 넣는다.
    /// keepBackground 인데 배경을 읽을 수 없으면 예외 "배경 이미지를 읽을 수 없습니다.".
    /// </summary>
    public static (LabelTemplate Tpl, int Taken, int Partial, int Dropped) Extract(LabelTemplate sheet, SKRect rectMm, bool includePartial, bool keepBackground)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var sheetLabel = sheet.Label ?? new LabelSize();
        var (rx, ry, rw, rh) = RectMm(rectMm);
        var takeFull = new List<LabelObject>();
        var takePartial = new List<LabelObject>();
        foreach (var o in sheet.Objects)
        {
            if (o is null) continue;
            if (Inside(o, rectMm)) takeFull.Add(o);
            else if (includePartial && Overlaps(o, rectMm)) takePartial.Add(o);
        }
        var src = takeFull.Concat(takePartial).ToList();
        var objects = new List<LabelObject>(src.Count);
        foreach (var o in src)
        {
            var c = Clone(o);
            c.X = JsRound((o.X - rx) * 100) / 100;
            c.Y = JsRound((o.Y - ry) * 100) / 100;
            objects.Add(c);
        }

        var bg = "";
        if (keepBackground && !string.IsNullOrEmpty(sheetLabel.Bg))
        {
            var src0 = LabelRenderer.GetImage(sheetLabel.Bg)
                       ?? throw new InvalidOperationException("배경 이미지를 읽을 수 없습니다.");
            using var cropped = CropBackground(src0, sheetLabel, rectMm)
                                ?? throw new InvalidOperationException("배경 이미지를 읽을 수 없습니다.");
            bg = ToDataUrl(cropped);
        }

        var tpl = new LabelTemplate
        {
            Name = sheet.Name ?? "",
            Label = new LabelSize
            {
                W = JsRound(rw * 100) / 100,
                H = JsRound(rh * 100) / 100,
                Bg = bg,
                BgInclude = sheetLabel.BgInclude,
            },
            Objects = objects,
        };
        return (tpl, takeFull.Count, takePartial.Count, sheet.Objects.Count - src.Count);
    }

    /// <summary>프리셋(Paper.ProductLabels 항목)으로 추출. 원판 좌표가 없으면 예외.</summary>
    public static (LabelTemplate, int, int, int) ExtractPreset(LabelTemplate sheet, LabelPreset preset, bool keepBackground)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (preset is null || preset.Src is null) throw new InvalidOperationException("이 프리셋에는 원판 좌표가 없습니다.");
        var (x, y) = preset.Src.Value;
        return Extract(sheet, SKRect.Create((float)x, (float)y, (float)preset.W, (float)preset.H), false, keepBackground);
    }

    /// <summary>원판 배경에서 영역만큼 잘라낸 비트맵 (흰 바탕). 배경이 없으면 null. px/mm = bg.Width / sheet.W.</summary>
    public static SKBitmap? CropBackground(SKBitmap bg, LabelSize sheet, SKRect rectMm)
    {
        if (bg is null) return null;
        ArgumentNullException.ThrowIfNull(sheet);
        var (rx, ry, rw, rh) = RectMm(rectMm);
        var sx = sheet.W > 0 ? bg.Width / sheet.W : 0;       // px per mm
        var sy = sheet.H > 0 ? bg.Height / sheet.H : 0;
        var w = Math.Max(1, (int)JsRound(rw * sx));
        var h = Math.Max(1, (int)JsRound(rh * sy));
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        var srcX = (float)JsRound(rx * sx);
        var srcY = (float)JsRound(ry * sy);
        using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        c.DrawBitmap(bg, SKRect.Create(srcX, srcY, w, h), SKRect.Create(0, 0, w, h), p);
        c.Flush();
        return bmp;
    }

    /// <summary>제품 라벨 프리셋별로 몇 개가 들어가고 몇 개가 걸치는지 미리보기 (선택 화면용).</summary>
    public static IReadOnlyList<(LabelPreset, int N, int Partial)> Preview(LabelTemplate sheet)
        => Preview(sheet, Paper.ProductLabels);

    /// <summary>주어진 프리셋 목록으로 미리보기. 원판 좌표가 없는 프리셋은 0.</summary>
    public static IReadOnlyList<(LabelPreset, int N, int Partial)> Preview(LabelTemplate sheet, IEnumerable<LabelPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(presets);
        var outList = new List<(LabelPreset, int, int)>();
        foreach (var p in presets)
        {
            if (p.Src is null) { outList.Add((p, 0, 0)); continue; }
            var r = SKRect.Create((float)p.Src.Value.X, (float)p.Src.Value.Y, (float)p.W, (float)p.H);
            int n = 0, partial = 0;
            foreach (var o in sheet.Objects)
            {
                if (o is null) continue;
                if (Inside(o, r)) n++;
                else if (Overlaps(o, r)) partial++;
            }
            outList.Add((p, n, partial));
        }
        return outList;
    }

    /* ---------------- 내부 ---------------- */

    // SKRect(float) → mm(double). 프리셋 좌표는 소수 1자리이므로 4자리 반올림으로 float 오차를 없앤다.
    private static (double X, double Y, double W, double H) RectMm(SKRect r)
        => (Math.Round((double)r.Left, 4), Math.Round((double)r.Top, 4),
            Math.Round((double)r.Width, 4), Math.Round((double)r.Height, 4));

    // JS Math.round — .5 는 +∞ 쪽으로
    private static double JsRound(double v) => Math.Floor(v + 0.5);

    private static string ToDataUrl(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
    }

    /// <summary>객체 깊은 복사 (JSON.parse(JSON.stringify(o)) 에 해당, 런타임 비트맵도 함께 넘긴다).</summary>
    public static LabelObject Clone(LabelObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        LabelObject c = o switch
        {
            TextObject t => new TextObject
            {
                Text = t.Text, Font = t.Font, SizePt = t.SizePt, Bold = t.Bold, Italic = t.Italic,
                LetterSpacing = t.LetterSpacing, WordSpacing = t.WordSpacing, Kerning = t.Kerning,
                LineHeight = t.LineHeight, HScale = t.HScale, Align = t.Align, VAlign = t.VAlign,
                Wrap = t.Wrap, AutoShrink = t.AutoShrink, Color = t.Color, Clip = t.Clip,
            },
            ImageObject im => new ImageObject
            {
                SourceField = im.SourceField, FileName = im.FileName, Fit = im.Fit, VFit = im.VFit, FitMode = im.FitMode,
                Bitmap = im.Bitmap, Error = im.Error, DataUrl = im.DataUrl,
            },
            BarcodeObject b => new BarcodeObject
            {
                Symbology = b.Symbology, Source = b.Source, Binding = b.Binding, Expression = b.Expression,
                LinkObjectId = b.LinkObjectId, HumanReadable = b.HumanReadable, FitMode = b.FitMode,
                Fit = b.Fit, VFit = b.VFit, Rotation = b.Rotation,
            },
            _ => throw new NotSupportedException($"알 수 없는 객체 종류입니다: \"{o.Type}\""),
        };
        c.Id = o.Id;
        c.Name = o.Name;
        c.X = o.X; c.Y = o.Y; c.W = o.W; c.H = o.H;
        c.Visible = o.Visible;
        c.Locked = o.Locked;
        return c;
    }
}
