// 렌더러 — RenderToBitmap 크기·바코드 왕복·텍스트 잉크, ★행마다 그림 교체 회귀, FitRect, 모듈 정수 배율, 배경, 유효 DPI.
using LaPrint.Core.Barcode;
using LaPrint.Core.Data;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Tests.Imaging;
using SkiaSharp;
using Xunit;
using static LaPrint.Core.Tests.Barcode.BarcodeTestUtil;

namespace LaPrint.Core.Tests.Render;

public class LabelRendererTests
{
    private const string Udi = "(01)08806367058034(10)LOT1(17)290531(240)42-0401(21)3";   // ZXing: 22×22 DataMatrix

    /* 60×30mm — 텍스트 {REF} · 스텐트 그림 · 딜리버리 그림 · GS1 DataMatrix(UDI_FULL) */
    private static LabelTemplate SmallTemplate() => new()
    {
        Name = "test",
        Label = new LabelSize { W = 60, H = 30 },
        Objects =
        {
            new TextObject { Id = "ref", X = 2, Y = 2, W = 36, H = 6, Text = "{REF}", Font = "Arial", SizePt = 8, Kerning = false },
            new ImageObject { Id = "stent", X = 2, Y = 10, W = 30, H = 8, SourceField = "IMG_STENT" },
            new ImageObject { Id = "delivery", X = 2, Y = 20, W = 30, H = 6, SourceField = "IMG_DELIVERY" },
            new BarcodeObject { Id = "bc", X = 44, Y = 2, W = 12, H = 12, Symbology = "gs1datamatrix", Source = "field", Binding = "UDI_FULL" },
        },
    };

    private static RenderContext CtxOf(LabelTemplate t, string item)
    {
        var row = SampleDb.Row(item);
        var f = SampleDb.FieldsOf(item);
        return new RenderContext
        {
            Fields = f, Row = row, Objects = t.Objects,
            ResolveText = s => Placeholders.Resolve(s, f, row),
        };
    }

    private static SKRectI PxRect(LabelObject o, double pxPerMm)
        => new((int)Math.Floor(o.X * pxPerMm), (int)Math.Floor(o.Y * pxPerMm),
               (int)Math.Ceiling((o.X + o.W) * pxPerMm), (int)Math.Ceiling((o.Y + o.H) * pxPerMm));

    private static bool IsDark(SKColor c) => c.Red < 100 && c.Green < 100 && c.Blue < 100;

    private static int CountDark(SKBitmap b, SKRectI r)
    {
        var n = 0;
        for (var y = Math.Max(0, r.Top); y < Math.Min(b.Height, r.Bottom); y++)
            for (var x = Math.Max(0, r.Left); x < Math.Min(b.Width, r.Right); x++)
                if (IsDark(b.GetPixel(x, y))) n++;
        return n;
    }

    private static int CountNonWhite(SKBitmap b, SKRectI r)
    {
        var n = 0;
        for (var y = Math.Max(0, r.Top); y < Math.Min(b.Height, r.Bottom); y++)
            for (var x = Math.Max(0, r.Left); x < Math.Min(b.Width, r.Right); x++)
            {
                var c = b.GetPixel(x, y);
                if (c.Red < 245 || c.Green < 245 || c.Blue < 245) n++;
            }
        return n;
    }

    /// <summary>영역 픽셀 바이트의 FNV-1a 64 해시.</summary>
    private static ulong RegionHash(SKBitmap b, params SKRectI[] rects)
    {
        var px = b.Bytes;
        var stride = b.RowBytes;
        var h = 14695981039346656037UL;
        foreach (var r in rects)
            for (var y = Math.Max(0, r.Top); y < Math.Min(b.Height, r.Bottom); y++)
            {
                var start = y * stride + Math.Max(0, r.Left) * 4;
                var end = y * stride + Math.Min(b.Width, r.Right) * 4;
                for (var i = start; i < end; i++) { h ^= px[i]; h *= 1099511628211UL; }
            }
        return h;
    }

    /* 영역을 흰 여백을 두고 잘라 낸다 (디코더용) */
    private static SKBitmap Crop(SKBitmap b, SKRectI r, int margin)
    {
        var o = new SKBitmap(r.Width + 2 * margin, r.Height + 2 * margin);
        using var c = new SKCanvas(o);
        c.Clear(SKColors.White);
        c.DrawBitmap(b, SKRect.Create(r.Left, r.Top, r.Width, r.Height), SKRect.Create(margin, margin, r.Width, r.Height));
        return o;
    }

    [Fact]
    public async Task RenderToBitmap_300dpi_SizeBarcodeText()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "16-0401");
        await SlotLoader.LoadIntoAsync(t.Objects, ctx.Fields, ctx.Row, SampleDb.NewStore(), true, true, 30);

        using var bmp = LabelRenderer.RenderToBitmap(t, ctx, 300);
        Assert.Equal(709, bmp.Width);
        Assert.Equal(354, bmp.Height);
        Assert.False(ctx.ForExport);           // 호출 전 값으로 되돌린다

        var pxPerMm = 300 / 25.4;
        var bc = t.Objects[3];
        var bcRect = PxRect(bc, pxPerMm);
        Assert.True(CountDark(bmp, bcRect) > 500, "바코드 영역에 검정 픽셀이 있어야 한다");

        using var crop = Crop(bmp, bcRect, 20);
        var r = Decode(crop, assumeGs1: true);
        Assert.True(r is not null, "바코드 영역을 디코드하지 못했습니다");
        Assert.Equal("]d2", r!.SymbologyId);
        var expected = Gs1.ToFnc1Stream(ctx.Fields["UDI_FULL"]);
        Assert.Contains("(01)", ctx.Fields["UDI_FULL"]);
        Assert.Equal(Show(expected), Show(StripGs1Prefix(r.Text, "gs1datamatrix")));

        var text = t.Objects[0];
        Assert.NotEqual("", ctx.ResolveText(((TextObject)text).Text));
        Assert.True(CountDark(bmp, PxRect(text, pxPerMm)) > 30, "텍스트 영역에 어두운 픽셀이 있어야 한다");

        // 그림 슬롯도 실제로 찍혔다
        Assert.True(CountNonWhite(bmp, PxRect(t.Objects[1], pxPerMm)) > 100);
        Assert.True(CountNonWhite(bmp, PxRect(t.Objects[2], pxPerMm)) > 100);
    }

    [Fact]
    public async Task 행마다_그림이_바뀐다()
    {
        var items = new[] { "16-0401", "42-0401", "21-1801" };
        var t = SmallTemplate();
        var store = SampleDb.NewStore();
        var pxPerMm = 300 / 25.4;
        var regions = new[] { PxRect(t.Objects[1], pxPerMm), PxRect(t.Objects[2], pxPerMm) };

        // 행마다 슬롯을 다시 읽으면 그림 영역이 서로 다르다
        var withReload = new List<ulong>();
        foreach (var item in items)
        {
            var ctx = CtxOf(t, item);
            await SlotLoader.LoadIntoAsync(t.Objects, ctx.Fields, ctx.Row, store, true, true, 30);
            using var bmp = LabelRenderer.RenderToBitmap(t, ctx, 300);
            Assert.True(CountNonWhite(bmp, regions[1]) > 100, $"{item}: 그림이 찍히지 않았다");
            withReload.Add(RegionHash(bmp, regions));
        }
        Assert.Equal(3, withReload.Distinct().Count());

        // 첫 건의 그림을 그대로 두고 데이터만 바꾸면 그림 영역은 똑같다 — 달라지는 것은 로더 때문이다
        var first = CtxOf(t, items[0]);
        await SlotLoader.LoadIntoAsync(t.Objects, first.Fields, first.Row, store, true, true, 30);
        var withoutReload = new List<ulong>();
        foreach (var item in items)
        {
            var ctx = CtxOf(t, item);
            using var bmp = LabelRenderer.RenderToBitmap(t, ctx, 300);
            withoutReload.Add(RegionHash(bmp, regions));
        }
        Assert.Single(withoutReload.Distinct());
        Assert.Equal(withReload[0], withoutReload[0]);
    }

    [Theory]
    [InlineData("contain", "center", "middle", 10, 32.5, 50, 25)]
    [InlineData("contain", "left", "top", 10, 20, 50, 25)]
    [InlineData("contain", "right", "bottom", 10, 45, 50, 25)]
    [InlineData("cover", "center", "middle", -15, 20, 100, 50)]
    [InlineData("cover", "left", "top", 10, 20, 100, 50)]
    [InlineData("cover", "right", "bottom", -40, 20, 100, 50)]
    [InlineData("stretch", "left", "top", 10, 20, 50, 50)]
    public void FitRect_Cases(string mode, string h, string v, double x, double y, double w, double hh)
    {
        var r = LabelRenderer.FitRect(200, 100, 10, 20, 50, 50, h, v, mode);
        Assert.Equal(x, r.Left, 4);
        Assert.Equal(y, r.Top, 4);
        Assert.Equal(w, r.Width, 4);
        Assert.Equal(hh, r.Height, 4);
    }

    [Fact]
    public void FitRect_TallSource_Contain()
    {
        // 100×200 을 50×50 에 contain → 25×50, 가로 가운데
        var r = LabelRenderer.FitRect(100, 200, 0, 0, 50, 50, "center", "middle", "contain");
        Assert.Equal(12.5, r.Left, 4);
        Assert.Equal(0, r.Top, 4);
        Assert.Equal(25, r.Width, 4);
        Assert.Equal(50, r.Height, 4);
    }

    [Fact]
    public void ModuleRule_2D_IntegerModules_Centered()
    {
        var sym = BarcodeEncoder.Encode("gs1datamatrix", Udi);
        Assert.Null(sym.Error);
        Assert.Equal(22, sym.ModulesW);
        Assert.Equal(1, sym.QuietZone);

        var o = new BarcodeObject { Id = "b", X = 0, Y = 0, W = 100, H = 20, Symbology = "gs1datamatrix" };
        using var bmp = new SKBitmap(1000, 200);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        var res = LabelRenderer.DrawBarcode(c, o, Udi, 10, 0, 0);
        Assert.True(res.Ok, res.Error);
        Assert.Equal(8, res.ModulePx, 6);          // floor(min(1000/24, 200/24)) = 8
        Assert.Equal(192, res.DrawnW, 6);          // 24 모듈 × 8px
        Assert.Equal(192, res.DrawnH, 6);
        Assert.Equal("ok", res.Quality);

        // 검정 픽셀의 경계 상자 = 조용한 영역 안쪽 22 모듈, 가운데 정렬 (dx = 404, dy = 4)
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                if (IsDark(bmp.GetPixel(x, y)))
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        Assert.Equal(404 + 8, minX);
        Assert.Equal(404 + 8 + 22 * 8 - 1, maxX);
        Assert.Equal(4 + 8, minY);
        Assert.Equal(4 + 8 + 22 * 8 - 1, maxY);

        // 왼쪽 위 모듈은 8×8 픽셀 모두 검정 (안티앨리어싱 없음) — DataMatrix L 패턴의 왼쪽 열
        for (var y = 12; y < 20; y++)
            for (var x = 412; x < 420; x++)
                Assert.Equal(SKColors.Black, bmp.GetPixel(x, y));

        // 그린 것을 다시 읽을 수 있다
        var r = Decode(bmp, assumeGs1: true);
        Assert.NotNull(r);
        Assert.Equal(Show(Gs1.ToFnc1Stream(Udi)), Show(StripGs1Prefix(r!.Text, "gs1datamatrix")));
    }

    [Fact]
    public void ModuleRule_1D_FillsHeight_And_Alignment()
    {
        var sym = BarcodeEncoder.Encode("gs1-128", "(01)08806367058034(10)LOT1");
        Assert.Null(sym.Error);
        var totalW = sym.ModulesW + 2 * sym.QuietZone;
        var o = new BarcodeObject { Id = "b", X = 0, Y = 0, W = 80, H = 15, Symbology = "gs1-128", Fit = "left", VFit = "top" };
        using var bmp = new SKBitmap(800, 150);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        var res = LabelRenderer.DrawBarcode(c, o, "(01)08806367058034(10)LOT1", 10, 0, 0);
        Assert.True(res.Ok, res.Error);
        var modPx = Math.Floor(800.0 / totalW);
        Assert.Equal(modPx, res.ModulePx, 6);
        Assert.Equal(totalW * modPx, res.DrawnW, 6);
        Assert.Equal(150, res.DrawnH, 6);
        // 왼쪽 정렬: 첫 막대는 조용한 영역(10모듈) 바로 뒤에서 시작하고 세로로 영역을 채운다
        var firstBar = (int)(10 * modPx);
        Assert.Equal(SKColors.Black, bmp.GetPixel(firstBar, 0));
        Assert.Equal(SKColors.Black, bmp.GetPixel(firstBar, 149));
        Assert.Equal(SKColors.White, bmp.GetPixel(firstBar - 1, 75));

        var r = Decode(bmp, assumeGs1: true);
        Assert.NotNull(r);
        Assert.Equal("]C1", r!.SymbologyId);
    }

    [Fact]
    public void Stretch_FillsRect_And_HumanReadable_DrawsText()
    {
        var o = new BarcodeObject { Id = "b", X = 0, Y = 0, W = 60, H = 20, Symbology = "gs1-128", FitMode = "stretch", HumanReadable = true };
        using var bmp = new SKBitmap(600, 200);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        var res = LabelRenderer.DrawBarcode(c, o, "(01)08806367058034(10)LOT1", 10, 0, 0);
        Assert.True(res.Ok, res.Error);
        Assert.Equal("stretched", res.Quality);
        Assert.Equal(600, res.DrawnW, 6);
        Assert.Equal(200, res.DrawnH, 6);
        // 아래쪽 문자 띠에 잉크가 있고, 막대는 문자 띠 위에서 끝난다
        Assert.True(CountDark(bmp, new SKRectI(0, 170, 600, 200)) > 20);
        var r = Decode(bmp, assumeGs1: true);
        Assert.NotNull(r);
    }

    [Fact]
    public void Barcode_EmptyData_And_Rotation()
    {
        var o = new BarcodeObject { Id = "b", X = 0, Y = 0, W = 20, H = 20, Symbology = "gs1datamatrix" };
        using var bmp = new SKBitmap(200, 200);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        var empty = LabelRenderer.DrawBarcode(c, o, "  ", 10, 0, 0);
        Assert.False(empty.Ok);
        Assert.Equal("데이터 없음", empty.Error);
        Assert.Equal(0, CountDark(bmp, new SKRectI(0, 0, 200, 200)));

        var bad = LabelRenderer.DrawBarcode(c, o, "(10)" + new string('A', 3000), 10, 0, 0);   // DataMatrix 용량 초과
        Assert.False(bad.Ok);
        Assert.False(string.IsNullOrEmpty(bad.Error));
        Assert.Equal(0, CountDark(bmp, new SKRectI(0, 0, 200, 200)));

        o.Rotation = 90;
        var rot = LabelRenderer.DrawBarcode(c, o, Udi, 10, 0, 0);
        Assert.True(rot.Ok);
        Assert.True(CountDark(bmp, new SKRectI(0, 0, 200, 200)) > 100);
        var r = Decode(bmp, assumeGs1: true);
        Assert.NotNull(r);
    }

    [Fact]
    public void DrawObject_Text_Image_Barcode_Results()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "42-0401");
        using var bmp = new SKBitmap(600, 300);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);

        var tr = LabelRenderer.DrawObject(c, t.Objects[0], ctx, 10, 0, 0);
        Assert.True(tr.Ok);
        Assert.NotNull(tr.Layout);

        // 그림 없음 → ImageMissing, 그림 있음(ImageOf) → Ok
        var ir = LabelRenderer.DrawObject(c, t.Objects[1], ctx, 10, 0, 0);
        Assert.False(ir.Ok);
        Assert.True(ir.ImageMissing);
        using var pic = ImageProcessor.Process(File.ReadAllBytes(Fixtures.Path(Path.Combine("images", "BNHS.jpg"))));
        ctx.ImageOf = id => id == "stent" ? pic : null;
        var ir2 = LabelRenderer.DrawObject(c, t.Objects[1], ctx, 10, 0, 0);
        Assert.True(ir2.Ok);
        Assert.True(ir2.DrawnW > 0);
        Assert.True(CountNonWhite(bmp, PxRect(t.Objects[1], 10)) > 100);

        var br = LabelRenderer.DrawObject(c, t.Objects[3], ctx, 10, 0, 0);
        Assert.True(br.Ok, br.Error);
        Assert.True(br.ModulePx >= 1);
    }

    [Fact]
    public void Background_TemplateBg_IsDrawnWhenIncluded()
    {
        var t = TemplateJson.Default();
        t.Objects.Clear();
        Assert.Equal(LabelSize.TemplateBg, t.Label.Bg);
        var ctx = new RenderContext { IncludeBg = true };
        using var with = LabelRenderer.RenderToBitmap(t, ctx, 20);
        Assert.Equal((int)Math.Round(297 / 25.4 * 20), with.Width);
        Assert.True(CountNonWhite(with, new SKRectI(0, 0, with.Width, with.Height)) > 100);

        ctx.IncludeBg = false;
        using var without = LabelRenderer.RenderToBitmap(t, ctx, 20);
        Assert.Equal(0, CountNonWhite(without, new SKRectI(0, 0, without.Width, without.Height)));

        // ctx.Background 가 우선한다
        using var red = new SKBitmap(2, 2);
        red.Erase(SKColors.Red);
        ctx.IncludeBg = true;
        ctx.Background = red;
        using var custom = LabelRenderer.RenderToBitmap(t, ctx, 20);
        Assert.Equal(SKColors.Red, custom.GetPixel(custom.Width / 2, custom.Height / 2));
    }

    [Fact]
    public void EffectiveDpi_CapsLargeLabels()
    {
        Assert.Equal((300, false), LabelRenderer.EffectiveDpi(new LabelSize { W = 60, H = 30 }, 300));
        Assert.Equal((450, true), LabelRenderer.EffectiveDpi(new LabelSize { W = 297, H = 420 }, 600));
        Assert.Equal((150, true), LabelRenderer.EffectiveDpi(new LabelSize { W = 2000, H = 2000 }, 600));

        var t = TemplateJson.Default();
        t.Objects.Clear();
        using var bmp = LabelRenderer.RenderToBitmap(t, new RenderContext { IncludeBg = false }, 600);
        Assert.Equal((int)Math.Round(297 / 25.4 * 450), bmp.Width);
        Assert.Equal((int)Math.Round(420 / 25.4 * 450), bmp.Height);
    }

    [Fact]
    public void Render_HiddenObjectsSkipped_And_Clipped()
    {
        var t = new LabelTemplate
        {
            Label = new LabelSize { W = 20, H = 10 },
            Objects =
            {
                new BarcodeObject { Id = "hidden", X = 1, Y = 1, W = 8, H = 8, Symbology = "datamatrix", Source = "expression", Expression = "A", Visible = false },
                new BarcodeObject { Id = "out", X = 25, Y = 1, W = 8, H = 8, Symbology = "datamatrix", Source = "expression", Expression = "B" },
            },
        };
        using var bmp = new SKBitmap(400, 100);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Blue);
        LabelRenderer.Render(c, t, new RenderContext { ForExport = true, Objects = t.Objects }, 10, 0, 0);
        Assert.Equal(0, CountDark(bmp, new SKRectI(0, 0, 400, 100)));
        Assert.Equal(SKColors.White, bmp.GetPixel(50, 50));       // 라벨 영역은 흰색
        Assert.Equal(SKColors.Blue, bmp.GetPixel(300, 50));       // 라벨 밖은 건드리지 않는다 (클립)
    }
}
