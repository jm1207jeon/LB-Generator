// 글리프 측정 — 글자(텍스트 요소) 단위 전진폭. 주 서체에 없는 글자는 대체 서체 런으로 나눠 재고 그린다.
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace LaPrint.Core.Typography;

/// <summary>같은 서체로 이어지는 글자 구간과 그 전진폭(px).</summary>
internal sealed class GlyphRun
{
    public required SKTypeface Face { get; init; }
    public required string Text { get; init; }
    public required int Start { get; init; }
    public required float[] Advances { get; init; }
    /// <summary>HarfBuzz 셰이핑 결과 (실패하면 null → 문자열 그대로 그린다).</summary>
    public ushort[]? Glyphs { get; init; }
    public SKPoint[]? Points { get; init; }
    public float Width { get; init; }
}

/// <summary>한 서체·크기에 대한 측정/그리기 세션. 대체 서체의 페인트를 함께 관리한다.</summary>
internal sealed class GlyphSession : IDisposable
{
    private static readonly object ShaperGate = new();
    private static readonly Dictionary<IntPtr, SKShaper> Shapers = new();
    private static bool _harfBuzzBroken;

    private readonly SKTypeface _primary;
    private readonly float _sizePx;
    private readonly bool _kerning;
    private readonly SKColor _color;
    private readonly Dictionary<IntPtr, SKPaint> _paints = new();

    public GlyphSession(SKTypeface primary, float sizePx, bool kerning, SKColor color)
    {
        _primary = primary;
        _sizePx = sizePx;
        _kerning = kerning;
        _color = color;
    }

    /// <summary>JS Array.from 과 같이 서로게이트 쌍을 한 글자로 나눈다.</summary>
    public static List<string> Chars(string s)
    {
        var list = new List<string>(s.Length);
        foreach (var r in s.EnumerateRunes()) list.Add(r.ToString());
        return list;
    }

    public SKPaint PaintOf(SKTypeface face)
    {
        if (_paints.TryGetValue(face.Handle, out var p)) return p;
        p = new SKPaint
        {
            Typeface = face,
            TextSize = _sizePx,
            TextScaleX = 1,
            IsAntialias = true,
            SubpixelText = true,
            HintingLevel = SKPaintHinting.NoHinting,
            Color = _color,
            TextAlign = SKTextAlign.Left,
        };
        _paints[face.Handle] = p;
        return p;
    }

    private SKTypeface FaceOf(string ch)
    {
        var rune = Rune.GetRuneAt(ch, 0);
        if (_primary.ContainsGlyph(rune.Value)) return _primary;
        /* 공백·제어문자처럼 어떤 서체에도 없는 글자는 주 서체로 둔다 */
        return FontProvider.FallbackFor(_primary, rune.Value);
    }

    /// <summary>글자 목록을 서체가 같은 런으로 나누고 각 글자의 전진폭(px)을 잰다.</summary>
    public List<GlyphRun> Runs(IReadOnlyList<string> chars)
    {
        var runs = new List<GlyphRun>();
        var i = 0;
        while (i < chars.Count)
        {
            var face = FaceOf(chars[i]);
            var j = i + 1;
            while (j < chars.Count && ReferenceEquals(FaceOf(chars[j]), face)) j++;
            var sb = new StringBuilder();
            for (var k = i; k < j; k++) sb.Append(chars[k]);
            runs.Add(MeasureRun(face, sb.ToString(), chars, i, j - i));
            i = j;
        }
        return runs;
    }

    /// <summary>글자마다의 전진폭(px). 자간·어간은 포함하지 않는다.</summary>
    public float[] Advances(IReadOnlyList<string> chars)
    {
        var adv = new float[chars.Count];
        foreach (var run in Runs(chars))
            Array.Copy(run.Advances, 0, adv, run.Start, run.Advances.Length);
        return adv;
    }

    private GlyphRun MeasureRun(SKTypeface face, string text, IReadOnlyList<string> chars, int start, int count)
    {
        var paint = PaintOf(face);
        if (_kerning)
        {
            var shaped = TryShape(face, text, paint, chars, start, count);
            if (shaped != null) return shaped;
        }
        var plain = PlainAdvances(paint, text, chars, start, count);
        return new GlyphRun { Face = face, Text = text, Start = start, Advances = plain, Width = Sum(plain) };
    }

    /* 커닝 끔: Skia 의 단순 전진폭 */
    private static float[] PlainAdvances(SKPaint paint, string text, IReadOnlyList<string> chars, int start, int count)
    {
        var w = paint.GetGlyphWidths(text);
        if (w.Length == count) return w;
        var adv = new float[count];
        for (var k = 0; k < count; k++)
        {
            var one = paint.GetGlyphWidths(chars[start + k]);
            adv[k] = one.Length > 0 ? one[0] : 0;
        }
        return adv;
    }

    private static float Sum(float[] a)
    {
        float s = 0;
        foreach (var v in a) s += v;
        return s;
    }

    /* 커닝 켬: HarfBuzz 셰이핑. 네이티브 라이브러리가 없거나 실패하면 null (→ 단순 전진폭) */
    private GlyphRun? TryShape(SKTypeface face, string text, SKPaint paint, IReadOnlyList<string> chars, int start, int count)
    {
        if (_harfBuzzBroken) return null;
        try
        {
            SKShaper shaper;
            lock (ShaperGate)
            {
                if (!Shapers.TryGetValue(face.Handle, out shaper!))
                {
                    shaper = new SKShaper(face);
                    Shapers[face.Handle] = shaper;
                }
            }
            using var sp = new SKPaint
            {
                Typeface = face, TextSize = _sizePx, TextScaleX = 1, TextEncoding = SKTextEncoding.Utf16,
            };
            var res = shaper.Shape(text, sp);
            var n = res.Codepoints.Length;
            if (n == 0 || res.Points.Length != n || res.Clusters.Length != n) return null;

            /* UTF-16 단위 → 글자 번호 */
            var unitToChar = new int[text.Length + 1];
            var pos = 0;
            for (var k = 0; k < count; k++)
            {
                for (var u = 0; u < chars[start + k].Length; u++) unitToChar[pos++] = k;
            }
            unitToChar[text.Length] = count - 1;

            var adv = new float[count];
            var glyphs = new ushort[n];
            var points = new SKPoint[n];
            for (var g = 0; g < n; g++)
            {
                var x = res.Points[g].X;
                var next = g + 1 < n ? res.Points[g + 1].X : res.Width;
                var cluster = (int)res.Clusters[g];
                if (cluster < 0 || cluster > text.Length) return null;
                adv[unitToChar[cluster]] += next - x;
                glyphs[g] = (ushort)res.Codepoints[g];
                points[g] = res.Points[g];
            }
            return new GlyphRun
            {
                Face = face, Text = text, Start = start, Advances = adv,
                Glyphs = glyphs, Points = points, Width = res.Width,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            _harfBuzzBroken = true;
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>런 하나를 (x, y) 에 그린다. 셰이핑된 런은 글리프 위치 그대로, 아니면 문자열로.</summary>
    public void DrawRun(SKCanvas c, GlyphRun run, float x, float y)
    {
        var paint = PaintOf(run.Face);
        if (run.Glyphs != null && run.Points != null && run.Glyphs.Length > 0)
        {
            using var font = paint.ToFont();
            using var builder = new SKTextBlobBuilder();
            var buf = builder.AllocatePositionedRun(font, run.Glyphs.Length);
            buf.SetGlyphs(run.Glyphs);
            buf.SetPositions(run.Points);
            using var blob = builder.Build();
            if (blob != null) c.DrawText(blob, x, y, paint);
            return;
        }
        c.DrawText(run.Text, x, y, paint);
    }

    /// <summary>'Hg한' 잉크 경계로 위/아래 높이(px)를 잰다 (JS actualBoundingBox 와 같은 뜻).</summary>
    public (float Asc, float Desc) InkMetrics(string probe)
    {
        float asc = 0, desc = 0;
        var chars = Chars(probe);
        foreach (var run in Runs(chars))
        {
            var bounds = SKRect.Empty;
            PaintOf(run.Face).MeasureText(run.Text, ref bounds);
            asc = Math.Max(asc, -bounds.Top);
            desc = Math.Max(desc, bounds.Bottom);
        }
        return (asc, desc);
    }

    public void Dispose()
    {
        foreach (var p in _paints.Values) p.Dispose();
        _paints.Clear();
    }
}
