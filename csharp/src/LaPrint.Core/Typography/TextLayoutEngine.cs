// 조판 엔진 — text.js 를 가장 정밀하게 옮긴다 (tokenize · 금칙 · CJK · autoShrink 이분탐색 · 정렬).
//
// 목표: 화면(px/mm)과 PDF 출력(dpi/25.4)에서 **완전히 동일한** 텍스트 레이아웃.
// 그래서 줄바꿈/정렬 계산은 mm 단위로 한 번만 하고(Layout), 그리기만 배율을 곱한다(Draw).
//
// 측정 규칙 — 잉크 폭(mm) = (Σ글리프 advance + 자간×(글자수−1) + 어간×(공백수)) / REF.
// 브라우저의 "후행 자간" 보정은 직접 합산하므로 필요 없다.
using System.Text;
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.Core.Typography;

/// <summary>텍스트 조판과 그리기. 조판은 mm 로 한 번만 하고 그리기만 배율을 곱한다.</summary>
public static class TextLayoutEngine
{
    /// <summary>pt → mm.</summary>
    public const double Pt2Mm = 25.4 / 72;

    /// <summary>측정 기준 배율 (px/mm).</summary>
    public const double Ref = 20;

    private const char Sep = '';

    private static readonly object Gate = new();
    private static readonly Dictionary<string, TextLayout> LayoutCache = new();
    private static readonly Dictionary<string, (double AscMm, double DescMm)> MetricCache = new();

    /* 줄 앞에 올 수 없는 문자 (금칙) */
    private const string NoLineStart = ".,;:!?)]}>」』】〉》”’%‰°′″℃、。・ー";

    /* CJK(한중일) 판별 — 공백 없이도 어디서나 줄바꿈 가능 */
    private static bool IsCjk(string ch)
    {
        var c = Rune.GetRuneAt(ch, 0).Value;
        return (c >= 0x1100 && c <= 0x11FF) || (c >= 0x2E80 && c <= 0x303F) || (c >= 0x3040 && c <= 0x30FF)
            || (c >= 0x3130 && c <= 0x318F) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF)
            || (c >= 0xA960 && c <= 0xA97F) || (c >= 0xAC00 && c <= 0xD7FF) || (c >= 0xF900 && c <= 0xFAFF)
            || (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);
    }

    private sealed record Token(string T, bool Brk, bool Sp = false, bool Cjk = false);

    /* 한 줄을 토큰으로 나눈다.
     *  - 공백: 그 뒤에서 줄바꿈 가능
     *  - CJK 글자: 한 글자가 토큰, 뒤에서 줄바꿈 가능
     *  - 그 외(영문/숫자): 단어 단위로 묶음 */
    private static List<Token> Tokenize(string str)
    {
        var outp = new List<Token>();
        var buf = new StringBuilder();
        void Flush()
        {
            if (buf.Length > 0) { outp.Add(new Token(buf.ToString(), false)); buf.Clear(); }
        }
        foreach (var ch in GlyphSession.Chars(str))
        {
            if (ch == " " || ch == "\t") { Flush(); outp.Add(new Token(ch, true, Sp: true)); }
            else if (IsCjk(ch)) { Flush(); outp.Add(new Token(ch, true, Cjk: true)); }
            else if (ch == "-" || ch == "/")
            {
                buf.Append(ch); Flush();
                if (outp.Count > 0) outp[^1] = outp[^1] with { Brk = true };
            }
            else buf.Append(ch);
        }
        Flush();
        return outp;
    }

    /* 토큰 목록을 maxW(mm) 안에서 줄로 접는다. */
    private static List<string> FoldTokens(List<Token> tokens, double maxW, Func<string, double> wOf)
    {
        var lines = new List<string>();
        var cur = "";
        for (var i = 0; i < tokens.Count; i++)
        {
            var tk = tokens[i];
            if (cur.Length == 0 && tk.Sp) continue;               // 줄 맨 앞 공백은 버린다
            var cand = cur + tk.T;
            var candW = wOf(cand);

            if (candW <= maxW || cur.Length == 0)
            {
                cur = cand;
                // 한 토큰 자체가 영역보다 길면 글자 단위로 강제 분해
                var chars = GlyphSession.Chars(tk.T);
                if (!tk.Sp && cur == tk.T && candW > maxW && chars.Count > 1)
                {
                    var part = "";
                    for (var k = 0; k < chars.Count; k++)
                    {
                        var test = part + chars[k];
                        if (wOf(test) > maxW && part.Length > 0) { lines.Add(part); part = chars[k]; }
                        else part = test;
                    }
                    cur = part;
                }
            }
            else if (tk.T.Length == 1 && NoLineStart.IndexOf(tk.T, StringComparison.Ordinal) >= 0)
            {
                cur = cand;                              // 금칙: 줄 앞 금지 문자는 붙여 둔다
            }
            else
            {
                lines.Add(cur.TrimEnd());
                cur = tk.Sp ? "" : tk.T;
            }
        }
        if (cur.Length > 0) lines.Add(cur.TrimEnd());
        return lines.Count > 0 ? lines : new List<string> { "" };
    }

    private static int CountSpaces(string s)
    {
        var n = 0;
        foreach (var c in s) if (c == ' ') n++;
        return n;
    }

    /* 문자열의 잉크 폭(px) — Σadvance + 자간×(글자수−1) + 어간×(공백수) */
    private static double InkWidthPx(GlyphSession s, string str, double lsPx, double wsPx)
    {
        if (string.IsNullOrEmpty(str)) return 0;
        var chars = GlyphSession.Chars(str);
        double w = 0;
        foreach (var a in s.Advances(chars)) w += a;
        w += lsPx * (chars.Count - 1);
        w += wsPx * CountSpaces(str);
        return w;
    }

    /* 폰트 수직 메트릭(mm). 캐시. */
    private static (double AscMm, double DescMm) Metrics(GlyphSession s, TextObject o, double fontPx)
    {
        var key = string.Join("|", FontName(o), o.Bold, o.Italic, fontPx.ToString("R"));
        if (MetricCache.TryGetValue(key, out var m)) return m;
        var (asc, desc) = s.InkMetrics("Hg한");
        var a = asc > 0 ? asc : fontPx * 0.8;
        var d = desc > 0 ? desc : fontPx * 0.2;
        m = (a / Ref, d / Ref);
        MetricCache[key] = m;
        if (MetricCache.Count > 400) MetricCache.Clear();
        return m;
    }

    private static string FontName(TextObject o) => string.IsNullOrEmpty(o.Font) ? "Arial" : o.Font;

    private sealed record Built(List<(string Text, double WMm)> Lines, double FontPx, double LsPx, double FontMm,
                                double LineHMm, double TotalHMm, double MaxLineW, (double AscMm, double DescMm) Met);

    /* 레이아웃 계산 — 배율과 무관한 mm 좌표계. */
    private static TextLayout LayoutRaw(TextObject o, string text)
    {
        var W = Math.Max(0.1, o.W);
        var H = Math.Max(0.1, o.H);
        var hs = o.HScale / 100;
        if (hs == 0 || double.IsNaN(hs)) hs = 1;
        var wrap = o.Wrap;
        var lhMul = o.LineHeight != 0 && !double.IsNaN(o.LineHeight) ? o.LineHeight : 1.15;

        // 압축을 고려한 실효 최대 폭: 가로로 hs배 눌리므로 원본 좌표계 한계는 W/hs
        var maxWEff = W / hs;
        var face = FontProvider.Get(FontName(o), o.Bold, o.Italic);

        Built Build(double sizePt)
        {
            var fontPx = sizePt * Pt2Mm * Ref;
            var lsPx = o.LetterSpacing * Pt2Mm * Ref;
            var wsPx = o.WordSpacing * Pt2Mm * Ref;
            using var s = new GlyphSession(face, (float)fontPx, o.Kerning, SKColors.Black);
            double WOf(string str) => InkWidthPx(s, str, lsPx, wsPx) / Ref;

            var paras = (text ?? "").Split('\n');
            var raw = new List<string>();
            foreach (var p in paras)
            {
                if (!wrap) { raw.Add(p); continue; }
                raw.AddRange(FoldTokens(Tokenize(p), maxWEff, WOf));
            }
            var lines = raw.Select(t => (Text: t, WMm: WOf(t))).ToList();
            var fontMm = sizePt * Pt2Mm;
            var met = Metrics(s, o, fontPx);
            var lineHMm = fontMm * lhMul;
            var totalHMm = (lines.Count - 1) * lineHMm + met.AscMm + met.DescMm;
            var maxLineW = lines.Count > 0 ? lines.Max(l => l.WMm) : 0;
            return new Built(lines, fontPx, lsPx, fontMm, lineHMm, totalHMm, maxLineW, met);
        }

        var sizePt = o.SizePt != 0 && !double.IsNaN(o.SizePt) ? o.SizePt : 8;
        var r = Build(sizePt);
        var shrunk = false;

        // 자동 축소: 이분 탐색 (줄바꿈이 크기에 의존하므로 매번 다시 접는다)
        if (o.AutoShrink && (r.TotalHMm > H + 0.01 || r.MaxLineW * hs > W + 0.01))
        {
            double lo = 1, hi = sizePt;
            double? best = null;
            for (var i = 0; i < 18 && hi - lo > 0.05; i++)
            {
                var mid = (lo + hi) / 2;
                var t = Build(mid);
                if (t.TotalHMm <= H && t.MaxLineW * hs <= W) { best = mid; lo = mid; }
                else hi = mid;
            }
            if (best != null)
            {
                sizePt = Math.Floor(best.Value * 10 + 0.5) / 10;
                r = Build(sizePt);
                shrunk = true;
            }
        }

        // 세로 정렬
        var vAlign = string.IsNullOrEmpty(o.VAlign) ? "top" : o.VAlign;
        double startY;
        if (vAlign == "middle") startY = (H - r.TotalHMm) / 2 + r.Met.AscMm;
        else if (vAlign == "bottom") startY = H - r.TotalHMm + r.Met.AscMm;
        else startY = r.Met.AscMm;

        // 가로 정렬 — 압축 후 시각 폭(wMm*hs) 기준으로 배치
        var align = string.IsNullOrEmpty(o.Align) ? "left" : o.Align;
        var n = r.Lines.Count;
        var outLines = new List<LayoutLine>(n);
        for (var i = 0; i < n; i++)
        {
            var l = r.Lines[i];
            var visW = l.WMm * hs;
            double xMm = 0, spaceExtra = 0;
            if (align == "center") xMm = (W - visW) / 2;
            else if (align == "right") xMm = W - visW;
            else if (align == "justify" && i < n - 1 && l.Text.IndexOf(' ') >= 0)
            {
                var gaps = CountSpaces(l.Text);
                if (gaps > 0) spaceExtra = (W - visW) / gaps / hs;   // 원본 좌표계 기준 추가 간격
            }
            outLines.Add(new LayoutLine(l.Text, xMm, l.WMm, visW, spaceExtra));
        }

        return new TextLayout(outLines, sizePt, r.FontMm, r.LineHMm, r.TotalHMm, startY, hs,
            OverflowX: r.MaxLineW * hs > W + 0.01,
            OverflowY: r.TotalHMm > H + 0.01,
            Shrunk: shrunk,
            AscMm: r.Met.AscMm, DescMm: r.Met.DescMm);
    }

    /* 레이아웃 캐시 — 같은 속성/문자열이면 재계산하지 않는다 */
    private static string CacheKey(TextObject o, string text)
    {
        var parts = new object[]
        {
            FontName(o), o.SizePt, o.Bold ? 1 : 0, o.Italic ? 1 : 0, o.LetterSpacing, o.WordSpacing,
            o.Kerning ? 1 : 0, o.LineHeight != 0 ? o.LineHeight : 1.15, o.HScale, o.Align, o.VAlign,
            o.Wrap ? 1 : 0, o.AutoShrink ? 1 : 0, Math.Round(o.W * 100), Math.Round(o.H * 100), text ?? "",
        };
        return string.Join(Sep, parts.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>조판 (캐시). 배율과 무관한 mm 좌표계.</summary>
    public static TextLayout Layout(TextObject o, string text)
    {
        lock (Gate)
        {
            var k = CacheKey(o, text);
            if (LayoutCache.TryGetValue(k, out var v)) return v;
            v = LayoutRaw(o, text ?? "");
            LayoutCache[k] = v;
            if (LayoutCache.Count > 800) LayoutCache.Clear();
            return v;
        }
    }

    /// <summary>그리기. 레이아웃은 mm, 여기서만 배율(px/mm)을 곱한다. ox·oy 는 px 오프셋.</summary>
    public static TextLayout Draw(SKCanvas c, TextObject o, string text, double scale, double ox, double oy)
    {
        var L = Layout(o, text);
        if (L.Lines.Count == 0) return L;
        lock (Gate)
        {
            var X = o.X * scale + ox;
            var Y = o.Y * scale + oy;
            var hs = L.HScale;

            c.Save();
            c.Translate((float)X, (float)Y);
            if (hs != 1) c.Scale((float)hs, 1);      // ★ 좌우 압축. 이후 u 좌표는 hs배로 눌려 그려진다

            if (!SKColor.TryParse(string.IsNullOrEmpty(o.Color) ? "#000" : o.Color, out var color)) color = SKColors.Black;
            var face = FontProvider.Get(FontName(o), o.Bold, o.Italic);
            var sizePx = L.SizePt * Pt2Mm * scale;
            var lsPx = o.LetterSpacing * Pt2Mm * scale;
            var wsPx = o.WordSpacing * Pt2Mm * scale;
            using var s = new GlyphSession(face, (float)sizePx, o.Kerning, color);

            for (var i = 0; i < L.Lines.Count; i++)
            {
                var ln = L.Lines[i];
                var u = (ln.XMm / hs) * scale;    // xMm은 압축 후 좌표 → 압축 전 좌표계로 환산
                var v = (L.StartYMm + i * L.LineHMm) * scale;
                if (ln.SpaceExtra != 0)
                {
                    var cx = u;
                    var parts = ln.Text.Split(' ');
                    var extra = ln.SpaceExtra * scale;
                    var spW = MeasureRun(s, " ", lsPx, wsPx);
                    for (var k = 0; k < parts.Length; k++)
                    {
                        DrawRun(c, s, parts[k], cx, v, lsPx, wsPx);
                        cx += MeasureRun(s, parts[k], lsPx, wsPx) + spW + extra + lsPx;
                    }
                }
                else
                {
                    DrawRun(c, s, ln.Text, u, v, lsPx, wsPx);
                }
            }
            c.Restore();
            return L;
        }
    }

    /* 글자 단위로 advance + 자간(공백 뒤에는 어간도) 만큼 전진하며 그린다.
     * 자간·어간이 없으면 런 통째로(셰이핑된 글리프 위치 그대로) 그린다. */
    private static void DrawRun(SKCanvas c, GlyphSession s, string str, double x, double y, double lsPx, double wsPx)
    {
        if (string.IsNullOrEmpty(str)) return;
        var chars = GlyphSession.Chars(str);
        var runs = s.Runs(chars);
        var cx = x;
        if (lsPx == 0 && wsPx == 0)
        {
            foreach (var run in runs)
            {
                s.DrawRun(c, run, (float)cx, (float)y);
                foreach (var a in run.Advances) cx += a;
            }
            return;
        }
        foreach (var run in runs)
        {
            var paint = s.PaintOf(run.Face);
            for (var k = 0; k < run.Advances.Length; k++)
            {
                var ch = chars[run.Start + k];
                c.DrawText(ch, (float)cx, (float)y, paint);
                cx += run.Advances[k] + lsPx;
                if (ch == " ") cx += wsPx;
            }
        }
    }

    /* 잉크 폭(px) — 자간은 글자 사이에만, 어간은 공백마다 */
    private static double MeasureRun(GlyphSession s, string str, double lsPx, double wsPx)
        => InkWidthPx(s, str, lsPx, wsPx);

    /// <summary>객체가 실제로 차지하는 영역(mm) — 넘침 검사/가이드용.</summary>
    public static (double WMm, double HMm, bool OverflowX, bool OverflowY, double SizePt, bool Shrunk, int Lines) Bounds(TextObject o, string text)
    {
        var L = Layout(o, text);
        var maxW = L.Lines.Count > 0 ? L.Lines.Max(l => l.VisWMm) : 0;
        return (maxW, L.TotalHMm, L.OverflowX, L.OverflowY, L.SizePt, L.Shrunk, L.Lines.Count);
    }

    /// <summary>조판 캐시를 비운다 (글꼴·객체 속성이 바뀐 뒤).</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            LayoutCache.Clear();
            MetricCache.Clear();
        }
    }
}
