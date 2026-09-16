// 용지 배치 — 골든 'paper'(75건) 의 Plan/Describe/SlotAt, 'presets' 표, 기본 배치값, MatchPreset, 재단선.
using System.Text.Json;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using SkiaSharp;
using Xunit;

namespace LaPrint.Core.Tests.Export;

public class PaperTests
{
    private static LayoutOptions OptOf(JsonElement e) => new()
    {
        Paper = e.GetProperty("paper").GetString() ?? "label",
        Orientation = e.GetProperty("orientation").GetString() ?? "auto",
        MarginMm = e.GetProperty("marginMm").GetDouble(),
        GapX = e.GetProperty("gapX").GetDouble(),
        GapY = e.GetProperty("gapY").GetDouble(),
        Align = e.GetProperty("align").GetString() ?? "center",
        CustomW = e.GetProperty("customW").GetDouble(),
        CustomH = e.GetProperty("customH").GetDouble(),
    };

    [Fact]
    public void Golden_Paper_Plan_Describe_Slot()
    {
        var cases = Fixtures.Golden().RootElement.GetProperty("paper");
        Assert.Equal(75, cases.GetArrayLength());
        foreach (var c in cases.EnumerateArray())
        {
            var label = new LabelSize { W = c.GetProperty("label").GetProperty("w").GetDouble(), H = c.GetProperty("label").GetProperty("h").GetDouble() };
            var opt = OptOf(c.GetProperty("opt"));
            var exp = c.GetProperty("plan");
            var plan = Paper.Plan(label, opt);
            var tag = $"{label.W}×{label.H} {opt.Paper}/{opt.Orientation}";

            Assert.True(Math.Abs(exp.GetProperty("paperW").GetDouble() - plan.PaperW) < 0.001, tag + " paperW");
            Assert.True(Math.Abs(exp.GetProperty("paperH").GetDouble() - plan.PaperH) < 0.001, tag + " paperH");
            Assert.Equal(exp.GetProperty("cols").GetInt32(), plan.Cols);
            Assert.Equal(exp.GetProperty("rows").GetInt32(), plan.Rows);
            Assert.Equal(exp.GetProperty("perPage").GetInt32(), plan.PerPage);
            Assert.True(Math.Abs(exp.GetProperty("originX").GetDouble() - plan.OriginX) < 0.001, tag + " originX");
            Assert.True(Math.Abs(exp.GetProperty("originY").GetDouble() - plan.OriginY) < 0.001, tag + " originY");
            Assert.True(Math.Abs(exp.GetProperty("stepX").GetDouble() - plan.StepX) < 0.001, tag + " stepX");
            Assert.True(Math.Abs(exp.GetProperty("stepY").GetDouble() - plan.StepY) < 0.001, tag + " stepY");
            Assert.Equal(exp.GetProperty("fits").GetBoolean(), plan.Fits);
            Assert.Equal(exp.GetProperty("direct").GetBoolean(), plan.Direct);
            if (exp.TryGetProperty("reason", out var reason)) Assert.Equal(reason.GetString(), plan.Reason);
            else Assert.Equal("", plan.Reason);

            Assert.Equal(c.GetProperty("describe").GetString(), Paper.Describe(label, opt));

            var slot1 = c.GetProperty("slot1");
            if (slot1.ValueKind == JsonValueKind.Object)
            {
                var (x, y) = Paper.SlotAt(plan, 1);
                Assert.True(Math.Abs(slot1.GetProperty("x").GetDouble() - x) < 0.001, tag + " slot1.x");
                Assert.True(Math.Abs(slot1.GetProperty("y").GetDouble() - y) < 0.001, tag + " slot1.y");
            }
        }
    }

    private static void AssertPresets(JsonElement arr, IReadOnlyList<LabelPreset> ours)
    {
        Assert.Equal(arr.GetArrayLength(), ours.Count);
        var i = 0;
        foreach (var e in arr.EnumerateArray())
        {
            var p = ours[i++];
            Assert.Equal(e.GetProperty("v").GetString(), p.Id);
            Assert.Equal(e.GetProperty("n").GetString(), p.Name);
            Assert.Equal(e.GetProperty("w").GetDouble(), p.W, 6);
            Assert.Equal(e.GetProperty("h").GetDouble(), p.H, 6);
            if (e.TryGetProperty("src", out var src))
            {
                Assert.NotNull(p.Src);
                Assert.Equal(src.GetProperty("x").GetDouble(), p.Src!.Value.X, 6);
                Assert.Equal(src.GetProperty("y").GetDouble(), p.Src!.Value.Y, 6);
            }
            else Assert.Null(p.Src);
        }
    }

    [Fact]
    public void Golden_Presets_Tables()
    {
        var g = Fixtures.Golden().RootElement.GetProperty("presets");
        AssertPresets(g.GetProperty("product"), Paper.ProductLabels);
        AssertPresets(g.GetProperty("common"), Paper.CommonLabels);
        Assert.Equal(12, Paper.ProductLabels.Count);
        Assert.Equal(9, Paper.CommonLabels.Count);

        var papers = g.GetProperty("papers");
        Assert.Equal(papers.GetArrayLength(), Paper.Papers.Count);
        var i = 0;
        foreach (var e in papers.EnumerateArray())
        {
            var p = Paper.Papers[i++];
            Assert.Equal(e.GetProperty("v").GetString(), p.Id);
            Assert.Equal(e.GetProperty("n").GetString(), p.Name);
            Assert.Equal(e.GetProperty("w").GetDouble(), p.W, 6);
            Assert.Equal(e.GetProperty("h").GetDouble(), p.H, 6);
        }

        var sheet = g.GetProperty("sheet");
        Assert.Equal(sheet.GetProperty("w").GetDouble(), Paper.SheetSize.W, 6);
        Assert.Equal(sheet.GetProperty("h").GetDouble(), Paper.SheetSize.H, 6);
        Assert.Equal(297, Paper.Sheet.W);
        Assert.Equal(420, Paper.Sheet.H);
        Assert.Equal("SHEET-A3", Paper.Sheet.Id);

        var d = g.GetProperty("defaultLayout");
        var o = new LayoutOptions();
        Assert.Equal(d.GetProperty("paper").GetString(), o.Paper);
        Assert.Equal(d.GetProperty("customW").GetDouble(), o.CustomW);
        Assert.Equal(d.GetProperty("customH").GetDouble(), o.CustomH);
        Assert.Equal(d.GetProperty("orientation").GetString(), o.Orientation);
        Assert.Equal(d.GetProperty("marginMm").GetDouble(), o.MarginMm);
        Assert.Equal(d.GetProperty("gapX").GetDouble(), o.GapX);
        Assert.Equal(d.GetProperty("gapY").GetDouble(), o.GapY);
        Assert.Equal(d.GetProperty("align").GetString(), o.Align);
        Assert.Equal(d.GetProperty("cropMarks").GetBoolean(), o.CropMarks);
        Assert.Equal(d.GetProperty("outline").GetBoolean(), o.Outline);
        Assert.Equal(d.GetProperty("repeat").GetString(), o.Repeat);
    }

    [Fact]
    public void MatchPreset_And_Lookup()
    {
        Assert.Equal("PMFL-001", Paper.MatchPreset(173.8, 75)?.Id);
        Assert.Equal("PMFL-001", Paper.MatchPreset(173.95, 74.8)?.Id);
        Assert.Null(Paper.MatchPreset(173.8, 75.4));
        Assert.Equal("SHEET-A3", Paper.MatchPreset(297, 420)?.Id);
        Assert.Equal("C-50x30", Paper.MatchPreset(50, 30)?.Id);
        Assert.Null(Paper.MatchPreset(1, 1));
        Assert.Equal("A4", Paper.PaperById("A4").Id);
        Assert.Equal("label", Paper.PaperById("nope").Id);
        Assert.Equal("BOX-PN", Paper.LabelById("BOX-PN")?.Id);
        Assert.Null(Paper.LabelById("nope"));
    }

    [Fact]
    public void Unknown_Paper_Is_Direct_And_Custom_Zero_Falls_Back()
    {
        var label = new LabelSize { W = 50, H = 30 };
        var direct = Paper.Plan(label, new LayoutOptions { Paper = "nope" });
        Assert.True(direct.Direct);
        Assert.Equal(50, direct.PaperW);

        var custom = Paper.Plan(label, new LayoutOptions { Paper = "custom", CustomW = 0, CustomH = 0, Orientation = "portrait" });
        Assert.False(custom.Direct);
        Assert.Equal(210, custom.PaperW);
        Assert.Equal(297, custom.PaperH);
    }

    [Fact]
    public void DrawCropMarks_Draws_Lines_Outside_Slot()
    {
        var label = new LabelSize { W = 50, H = 30 };
        var plan = Paper.Plan(label, new LayoutOptions { Paper = "A4", Orientation = "portrait" });
        // 선 두께가 max(0.5, 0.2×pxPerMm)px 이고 선 중심이 화소 경계에 정확히 걸치므로,
        // 한 화소가 확실히 덮이도록 8px/mm(≈203dpi)로 그려서 살핀다.
        const double pxPerMm = 8;
        using var bmp = new SKBitmap((int)(plan.PaperW * pxPerMm), (int)(plan.PaperH * pxPerMm));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        Paper.DrawCropMarks(c, plan, 0, label, pxPerMm);
        c.Flush();

        var (sx, sy) = Paper.SlotAt(plan, 0);
        // 좌상단 모서리 왼쪽 2mm 지점에는 선이 있고, 칸 안쪽에는 없다
        var onMark = bmp.GetPixel((int)((sx - 2) * pxPerMm), (int)(sy * pxPerMm));
        var insideSlot = bmp.GetPixel((int)((sx + 10) * pxPerMm), (int)((sy + 10) * pxPerMm));
        Assert.True(onMark.Red < 128, "재단선이 있어야 한다");
        Assert.Equal(SKColors.White, insideSlot);
    }
}
