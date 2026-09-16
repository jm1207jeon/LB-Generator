// 조판 엔진 성질 검사 — 자간·어간·장평·줄바꿈·금칙·autoShrink·세로 정렬·그리기 (Liberation Sans / Arial).
using LaPrint.Core.Model;
using LaPrint.Core.Typography;
using SkiaSharp;
using Xunit;

namespace LaPrint.Core.Tests.Typography;

public class TextLayoutEngineTests
{
    private const double Pt2Mm = TextLayoutEngine.Pt2Mm;

    /// <summary>리눅스 CI 는 Liberation Sans, Windows 는 Arial.</summary>
    internal static string TestFont
        => FontProvider.IsAvailable("Liberation Sans") ? "Liberation Sans" : "Arial";

    private static TextObject Obj(string text, double w = 100, double h = 30, bool wrap = false)
        => new()
        {
            Id = "t1", X = 0, Y = 0, W = w, H = h, Text = text, Font = TestFont, SizePt = 10,
            Wrap = wrap, Kerning = false,
        };

    private static double Width(TextObject o) => TextLayoutEngine.Bounds(o, o.Text).WMm;

    [Fact]
    public void LetterSpacing_AppliesBetweenCharsOnly()
    {
        var a = Obj("ABCDE");
        var b = Obj("ABCDE");
        b.LetterSpacing = 2;
        var diff = Width(b) - Width(a);
        Assert.InRange(diff, 4 * 2 * Pt2Mm - 1e-6, 4 * 2 * Pt2Mm + 1e-6);   // 5 가 아니라 4 칸 — 마지막 글자 뒤 자간 없음
    }

    [Fact]
    public void LetterSpacing_Kerning_On_AlsoBetweenCharsOnly()
    {
        var a = Obj("ABCDE"); a.Kerning = true;
        var b = Obj("ABCDE"); b.Kerning = true; b.LetterSpacing = 3;
        var diff = Width(b) - Width(a);
        Assert.InRange(diff, 4 * 3 * Pt2Mm - 1e-6, 4 * 3 * Pt2Mm + 1e-6);
    }

    [Fact]
    public void HScale_HalvesVisualWidthOnly()
    {
        var a = Obj("Hello World");
        var b = Obj("Hello World");
        b.HScale = 50;
        var la = TextLayoutEngine.Layout(a, a.Text);
        var lb = TextLayoutEngine.Layout(b, b.Text);
        Assert.Equal(la.Lines[0].VisWMm / 2, lb.Lines[0].VisWMm, 9);
        Assert.Equal(la.Lines[0].WMm, lb.Lines[0].WMm, 9);
        Assert.Equal(la.TotalHMm, lb.TotalHMm, 9);
        Assert.Equal(la.LineHMm, lb.LineHMm, 9);
        Assert.Equal(la.AscMm, lb.AscMm, 9);
        Assert.Equal(0.5, lb.HScale, 9);
    }

    [Fact]
    public void WordSpacing_AddsPerSpace()
    {
        var a = Obj("aa bb cc");
        var b = Obj("aa bb cc");
        b.WordSpacing = 3;
        var diff = Width(b) - Width(a);
        Assert.InRange(diff, 2 * 3 * Pt2Mm - 1e-6, 2 * 3 * Pt2Mm + 1e-6);
    }

    [Fact]
    public void Wrap_BreaksAtSpaces()
    {
        var wAll = Width(Obj("aaa bbb ccc"));
        var wTwo = Width(Obj("aaa bbb"));
        Assert.True(wTwo < wAll);
        var o = Obj("aaa bbb ccc", w: (wAll + wTwo) / 2, wrap: true);
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.Equal(new[] { "aaa bbb", "ccc" }, L.Lines.Select(l => l.Text).ToArray());
        Assert.False(L.OverflowX);
    }

    [Fact]
    public void Wrap_ForceSplitsOverlongToken()
    {
        var w3 = Width(Obj("abc"));
        var w4 = Width(Obj("abcd"));
        var boxW = (w3 + w4) / 2;
        var o = Obj("abcdefghij", w: boxW, wrap: true);
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.True(L.Lines.Count > 1);
        Assert.Equal("abcdefghij", string.Concat(L.Lines.Select(l => l.Text)));
        Assert.Equal("abc", L.Lines[0].Text);
        foreach (var l in L.Lines) Assert.True(l.VisWMm <= boxW + 1e-9, l.Text);   // 글자 단위로 잘려 모두 영역 안
        Assert.False(L.OverflowX);
    }

    [Fact]
    public void Wrap_KoreanBreaksPerCharacter()
    {
        var w3 = Width(Obj("가나다"));
        var w4 = Width(Obj("가나다라"));
        Assert.True(w3 > 0 && w4 > w3);
        var o = Obj("가나다라마바사", w: (w3 + w4) / 2, wrap: true);
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.Equal(new[] { "가나다", "라마바", "사" }, L.Lines.Select(l => l.Text).ToArray());
    }

    [Fact]
    public void Wrap_NoLineStart_KeepsPunctuationAttached()
    {
        var w3 = Width(Obj("가나다"));
        var w4 = Width(Obj("가나다,"));
        var o = Obj("가나다,", w: (w3 + w4) / 2, wrap: true);
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.Equal(new[] { "가나다," }, L.Lines.Select(l => l.Text).ToArray());

        var wa = Width(Obj("abc "));
        var wb = Width(Obj("abc ,"));
        var p = Obj("abc ,", w: (wa + wb) / 2, wrap: true);
        var Lp = TextLayoutEngine.Layout(p, p.Text);
        Assert.Equal(new[] { "abc ," }, Lp.Lines.Select(l => l.Text).ToArray());
    }

    [Fact]
    public void Wrap_ParagraphBreakAndLeadingSpaces()
    {
        var o = Obj("ab\n  cd", wrap: true);
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.Equal(new[] { "ab", "cd" }, L.Lines.Select(l => l.Text).ToArray());
    }

    [Fact]
    public void AutoShrink_ReducesUntilFits()
    {
        var o = Obj("Hello World Hello", w: 20, h: 5, wrap: true);
        o.SizePt = 30; o.AutoShrink = true;
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.True(L.Shrunk);
        Assert.True(L.SizePt < 30 && L.SizePt >= 1);
        Assert.Equal(Math.Round(L.SizePt * 10) / 10, L.SizePt, 9);     // 0.1pt 단위
        Assert.False(L.OverflowX);
        Assert.False(L.OverflowY);

        var no = Obj("Hello World Hello", w: 20, h: 5, wrap: true);
        no.SizePt = 30;
        Assert.True(TextLayoutEngine.Layout(no, no.Text).OverflowX || TextLayoutEngine.Layout(no, no.Text).OverflowY);
    }

    [Fact]
    public void AutoShrink_NeverBelowOnePt()
    {
        var o = Obj("WWWWWWWWWW", w: 0.3, h: 0.3, wrap: false);
        o.SizePt = 12; o.AutoShrink = true;
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.True(L.SizePt >= 1);
        Assert.False(L.Shrunk);       // 1pt 로도 못 들어가면 원래 크기 유지
        Assert.Equal(12, L.SizePt);
    }

    [Fact]
    public void VAlign_MiddleCenters()
    {
        var o = Obj("Hg", h: 30);
        o.VAlign = "middle";
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.Equal((30 - L.TotalHMm) / 2 + L.AscMm, L.StartYMm, 9);

        var b = Obj("Hg", h: 30); b.VAlign = "bottom";
        var Lb = TextLayoutEngine.Layout(b, b.Text);
        Assert.Equal(30 - Lb.TotalHMm + Lb.AscMm, Lb.StartYMm, 9);

        var t = Obj("Hg", h: 30);
        Assert.Equal(TextLayoutEngine.Layout(t, t.Text).AscMm, TextLayoutEngine.Layout(t, t.Text).StartYMm, 9);
    }

    [Fact]
    public void Align_CenterRightJustify()
    {
        var c = Obj("abc def", w: 50); c.Align = "center";
        var Lc = TextLayoutEngine.Layout(c, c.Text);
        Assert.Equal((50 - Lc.Lines[0].VisWMm) / 2, Lc.Lines[0].XMm, 9);

        var r = Obj("abc def", w: 50); r.Align = "right";
        var Lr = TextLayoutEngine.Layout(r, r.Text);
        Assert.Equal(50 - Lr.Lines[0].VisWMm, Lr.Lines[0].XMm, 9);

        var j = Obj("aaa bbb ccc\nddd", w: 50, wrap: true); j.Align = "justify";
        var Lj = TextLayoutEngine.Layout(j, j.Text);
        Assert.Equal(2, Lj.Lines.Count);
        Assert.Equal((50 - Lj.Lines[0].VisWMm) / 2, Lj.Lines[0].SpaceExtra, 9);
        Assert.Equal(0, Lj.Lines[1].SpaceExtra);     // 마지막 줄은 양끝 정렬하지 않는다
    }

    [Fact]
    public void Draw_ProducesInkOfExpectedWidth()
    {
        var o = Obj("HHHHHHHH", w: 40, h: 12);
        o.SizePt = 12;
        const double scale = 5;
        var L = TextLayoutEngine.Layout(o, o.Text);
        using var bmp = new SKBitmap(200, 60);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            TextLayoutEngine.Draw(c, o, o.Text, scale, 0, 0);
        }
        int minX = int.MaxValue, maxX = -1, ink = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Red < 128 && px.Green < 128 && px.Blue < 128)
                {
                    ink++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            }
        Assert.True(ink > 0);
        var inkW = maxX - minX + 1;
        var expected = L.Lines[0].VisWMm * scale;
        Assert.InRange(inkW, expected * 0.92, expected * 1.08);
    }

    [Fact]
    public void Draw_HScale_HalvesInkWidth()
    {
        var o = Obj("HHHHHHHH", w: 40, h: 12); o.SizePt = 12;
        var h = Obj("HHHHHHHH", w: 40, h: 12); h.SizePt = 12; h.HScale = 50;
        Assert.InRange(InkWidthPx(h, 5), InkWidthPx(o, 5) * 0.42, InkWidthPx(o, 5) * 0.58);
    }

    [Fact]
    public void Draw_KoreanRendersInk()
    {
        var o = Obj("한글 라벨", w: 40, h: 12); o.SizePt = 12;
        Assert.True(InkWidthPx(o, 5) > 10);
    }

    private static int InkWidthPx(TextObject o, double scale)
    {
        using var bmp = new SKBitmap(200, 60);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            TextLayoutEngine.Draw(c, o, o.Text, scale, 0, 0);
        }
        int minX = int.MaxValue, maxX = -1;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Red < 128 && px.Green < 128 && px.Blue < 128)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            }
        return maxX < 0 ? 0 : maxX - minX + 1;
    }

    [Fact]
    public void Layout_IsScaleInvariant()
    {
        var o = Obj("Scale invariant 조판", w: 60, h: 20, wrap: true);
        o.LetterSpacing = 1; o.WordSpacing = 2; o.Align = "center";
        var before = TextLayoutEngine.Layout(o, o.Text);
        using var bmp = new SKBitmap(300, 100);
        using var c = new SKCanvas(bmp);
        var d1 = TextLayoutEngine.Draw(c, o, o.Text, 3, 0, 0);
        var d2 = TextLayoutEngine.Draw(c, o, o.Text, 11.8, 5, 5);
        Assert.Same(before, d1);
        Assert.Same(before, d2);

        TextLayoutEngine.Invalidate();
        var again = TextLayoutEngine.Layout(o, o.Text);
        Assert.NotSame(before, again);
        Assert.Equal(before.Lines.Select(l => l.Text), again.Lines.Select(l => l.Text));
        for (var i = 0; i < before.Lines.Count; i++)
        {
            Assert.Equal(before.Lines[i].XMm, again.Lines[i].XMm, 9);
            Assert.Equal(before.Lines[i].VisWMm, again.Lines[i].VisWMm, 9);
        }
        Assert.Equal(before.StartYMm, again.StartYMm, 9);
        Assert.Equal(before.TotalHMm, again.TotalHMm, 9);
    }

    [Fact]
    public void Bounds_OverflowX_WhenNoWrap()
    {
        var o = Obj("This text is much wider than the box", w: 10, h: 10, wrap: false);
        var b = TextLayoutEngine.Bounds(o, o.Text);
        Assert.True(b.OverflowX);
        Assert.True(b.WMm > 10);
        Assert.Equal(1, b.Lines);

        var w = Obj("This text is much wider than the box", w: 10, h: 50, wrap: true);
        var bw = TextLayoutEngine.Bounds(w, w.Text);
        Assert.True(bw.Lines > 1);
        Assert.False(bw.OverflowY);
    }

    [Fact]
    public void Layout_LineHeightAndTotalHeight()
    {
        var o = Obj("a\nb\nc", h: 30);
        o.LineHeight = 1.5;
        var L = TextLayoutEngine.Layout(o, o.Text);
        Assert.Equal(3, L.Lines.Count);
        Assert.Equal(10 * Pt2Mm, L.FontMm, 9);
        Assert.Equal(10 * Pt2Mm * 1.5, L.LineHMm, 9);
        Assert.Equal(2 * L.LineHMm + L.AscMm + L.DescMm, L.TotalHMm, 9);
    }
}
