// 원판에서 떼어내기 — 골든 'extract' (프리셋별 id 집합), 좌표 이동, 배경 자르기, 미리보기.
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using SkiaSharp;
using Xunit;

namespace LaPrint.Core.Tests.Export;

public class SheetExtractorTests
{
    private static SKRect RectOf(LabelPreset p)
        => SKRect.Create((float)p.Src!.Value.X, (float)p.Src.Value.Y, (float)p.W, (float)p.H);

    [Fact]
    public void Golden_Extract_Ids_And_Partial()
    {
        var sheet = TemplateJson.Default();
        var cases = Fixtures.Golden().RootElement.GetProperty("extract");
        Assert.Equal(12, cases.GetArrayLength());
        foreach (var c in cases.EnumerateArray())
        {
            var id = c.GetProperty("preset").GetString();
            var preset = Paper.ProductLabels.Single(p => p.Id == id);
            var r = RectOf(preset);
            var expIds = c.GetProperty("ids").EnumerateArray().Select(e => e.GetString()!).ToList();
            var expPartial = c.GetProperty("partial").EnumerateArray().Select(e => e.GetString()!).ToList();

            var ids = sheet.Objects.Where(o => SheetExtractor.Inside(o, r)).Select(o => o.Id).ToList();
            var partial = sheet.Objects.Where(o => !SheetExtractor.Inside(o, r) && SheetExtractor.Overlaps(o, r)).Select(o => o.Id).ToList();
            Assert.Equal(expIds, ids);
            Assert.Equal(expPartial, partial);

            // Extract(includePartial) 은 안쪽 객체 뒤에 걸친 객체를 붙인다
            var (tpl, taken, part, dropped) = SheetExtractor.Extract(sheet, r, true, false);
            Assert.Equal(expIds.Concat(expPartial).ToList(), tpl.Objects.Select(o => o.Id).ToList());
            Assert.Equal(expIds.Count, taken);
            Assert.Equal(expPartial.Count, part);
            Assert.Equal(sheet.Objects.Count - expIds.Count - expPartial.Count, dropped);
        }
    }

    [Fact]
    public void Golden_Extract_Matches_Preview()
    {
        var sheet = TemplateJson.Default();
        var cases = Fixtures.Golden().RootElement.GetProperty("extract").EnumerateArray()
            .ToDictionary(c => c.GetProperty("preset").GetString()!, c => c);
        var pv = SheetExtractor.Preview(sheet);
        Assert.Equal(Paper.ProductLabels.Count, pv.Count);
        foreach (var (preset, n, partial) in pv)
        {
            var c = cases[preset.Id];
            Assert.Equal(c.GetProperty("ids").GetArrayLength(), n);
            Assert.Equal(c.GetProperty("partial").GetArrayLength(), partial);
        }
    }

    [Fact]
    public void ExtractPreset_Shifts_Coordinates_And_Sets_Label_Size()
    {
        var sheet = TemplateJson.Default();
        var preset = Paper.ProductLabels.Single(p => p.Id == "PMFL-001");
        var (tpl, taken, partial, _) = SheetExtractor.ExtractPreset(sheet, preset, false);
        Assert.Equal(173.8, tpl.Label.W, 6);
        Assert.Equal(75, tpl.Label.H, 6);
        Assert.Equal("", tpl.Label.Bg);
        Assert.Equal(0, partial);
        Assert.True(taken > 0);
        Assert.Equal(sheet.Label.BgInclude, tpl.Label.BgInclude);

        foreach (var o in tpl.Objects)
        {
            var src = sheet.Objects.Single(s => s.Id == o.Id);
            Assert.NotSame(src, o);                                      // 깊은 복사
            Assert.Equal(Math.Floor((src.X - 59.5) * 100 + 0.5) / 100, o.X, 9);
            Assert.Equal(Math.Floor((src.Y - 318.0) * 100 + 0.5) / 100, o.Y, 9);
            Assert.Equal(src.W, o.W);
            Assert.Equal(src.H, o.H);
            Assert.Equal(src.Type, o.Type);
            Assert.True(o.X >= -0.8 && o.Y >= -0.8 && o.X + o.W <= 173.8 + 0.8 && o.Y + o.H <= 75 + 0.8);
        }
        // 원본은 그대로
        Assert.Contains(sheet.Objects, o => o.Y > 300);
    }

    [Fact]
    public void ExtractPreset_Without_Src_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SheetExtractor.ExtractPreset(TemplateJson.Default(), Paper.CommonLabels[0], false));
        Assert.Equal("이 프리셋에는 원판 좌표가 없습니다.", ex.Message);
    }

    [Fact]
    public void Inside_Tolerance_And_Overlaps()
    {
        var r = SKRect.Create(10, 10, 50, 20);
        var o = new TextObject { X = 9.5, Y = 9.5, W = 51, H = 21 };
        Assert.True(SheetExtractor.Inside(o, r));            // 0.8mm 허용
        Assert.False(SheetExtractor.Inside(o, r, 0.2));
        Assert.True(SheetExtractor.Overlaps(o, r));
        var touching = new TextObject { X = 60, Y = 10, W = 5, H = 5 };   // 오른쪽 변에 닿기만
        Assert.False(SheetExtractor.Overlaps(touching, r));
        Assert.False(SheetExtractor.Inside(touching, r));
        var far = new TextObject { X = 100, Y = 100, W = 5, H = 5 };
        Assert.False(SheetExtractor.Overlaps(far, r));
    }

    [Fact]
    public void CropBackground_Uses_Sheet_Scale()
    {
        // 297×420 원판이 594×840 px → 2 px/mm. 왼쪽 위 (10,20)~(60,40) mm 를 자르면 100×40 px
        using var bg = new SKBitmap(594, 840);
        using (var c = new SKCanvas(bg))
        {
            c.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            c.DrawRect(SKRect.Create(20, 40, 100, 40), red);      // 정확히 잘라낼 영역
        }
        var sheet = new LabelSize { W = 297, H = 420 };
        using var crop = SheetExtractor.CropBackground(bg, sheet, SKRect.Create(10, 20, 50, 20));
        Assert.NotNull(crop);
        Assert.Equal(100, crop!.Width);
        Assert.Equal(40, crop.Height);
        Assert.Equal(SKColors.Red, crop.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, crop.GetPixel(99, 39));
        Assert.Equal(SKColors.Red, crop.GetPixel(50, 20));
        Assert.Null(SheetExtractor.CropBackground(null!, sheet, SKRect.Create(0, 0, 1, 1)));
    }

    [Fact]
    public void Extract_KeepBackground_Produces_DataUrl_Of_Cropped_Size()
    {
        var sheet = TemplateJson.Default();
        Assert.Equal(LabelSize.TemplateBg, sheet.Label.Bg);
        var preset = Paper.ProductLabels.Single(p => p.Id == "PML-001");
        var (tpl, _, _, _) = SheetExtractor.ExtractPreset(sheet, preset, true);
        Assert.StartsWith("data:image/png;base64,", tpl.Label.Bg);

        using var full = SKBitmap.Decode(TemplateJson.LoadBackgroundPng());
        using var cropped = SKBitmap.Decode(Convert.FromBase64String(tpl.Label.Bg["data:image/png;base64,".Length..]));
        Assert.NotNull(cropped);
        var sx = full.Width / 297.0;
        var sy = full.Height / 420.0;
        Assert.Equal((int)Math.Floor(preset.W * sx + 0.5), cropped!.Width);
        Assert.Equal((int)Math.Floor(preset.H * sy + 0.5), cropped.Height);
    }
}
