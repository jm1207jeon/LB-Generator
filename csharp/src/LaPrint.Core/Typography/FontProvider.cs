// 글꼴 공급 — 요청 글꼴이 없으면 계량이 비슷한 대체 글꼴(Windows 맑은 고딕/Arial · Linux Liberation Sans).
using System.Text;
using SkiaSharp;

namespace LaPrint.Core.Typography;

/// <summary>글꼴 선택과 폴백.</summary>
public static class FontProvider
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> Cache = new();
    private static readonly Dictionary<(IntPtr Face, int Codepoint), SKTypeface> CharCache = new();

    /* 요청 글꼴이 없을 때 차례로 시도하는 대체 글꼴 (Windows → Linux 순) */
    private static readonly string[] FallbackChain =
    {
        "Arial", "Liberation Sans", "Malgun Gothic", "Noto Sans CJK KR", "WenQuanYi Zen Hei",
    };

    /// <summary>글꼴 패밀리·굵기·기울임에 맞는 서체. 없으면 폴백 사슬을 따라가고 마지막에는 기본 서체.</summary>
    public static SKTypeface Get(string family, bool bold, bool italic)
    {
        var fam = string.IsNullOrWhiteSpace(family) ? "Arial" : family.Trim();
        var key = (fam, bold, italic);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var hit)) return hit;

            var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            SKTypeface? found = TryFamily(fam, weight, slant);
            if (found == null)
            {
                foreach (var alt in FallbackChain)
                {
                    found = TryFamily(alt, weight, slant);
                    if (found != null) break;
                }
            }
            found ??= SKTypeface.Default;
            Cache[key] = found;
            return found;
        }
    }

    /* 해당 패밀리가 실제로 있을 때만 서체를 돌려준다 (Skia 의 조용한 기본 서체 대체를 걸러낸다) */
    private static SKTypeface? TryFamily(string family, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        try
        {
            var tf = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, slant);
            if (tf == null) return null;
            if (SameFamily(tf.FamilyName, family)) return tf;
            tf.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool SameFamily(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>시스템에 실제로 설치된 글꼴인지 확인 (폴백 감지).</summary>
    public static bool IsAvailable(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return false;
        try
        {
            using var tf = SKFontManager.Default.MatchFamily(family.Trim());
            return tf != null && SameFamily(tf.FamilyName, family);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>주 서체에 없는 첫 글자(한글 등)를 가진 대체 서체. 모두 있으면 주 서체 그대로.</summary>
    public static SKTypeface CjkFallback(SKTypeface primary, string text)
    {
        if (string.IsNullOrEmpty(text)) return primary;
        foreach (var rune in text.EnumerateRunes())
        {
            if (primary.ContainsGlyph(rune.Value)) continue;
            return FallbackFor(primary, rune.Value);
        }
        return primary;
    }

    /// <summary>코드포인트 하나에 대한 대체 서체 (캐시). 어떤 서체에도 없으면 주 서체.</summary>
    internal static SKTypeface FallbackFor(SKTypeface primary, int codepoint)
    {
        lock (Gate)
        {
            var key = (primary.Handle, codepoint);
            if (CharCache.TryGetValue(key, out var hit)) return hit;
            var result = MatchCharacter(primary, codepoint) ?? ScanFamilies(primary, codepoint) ?? primary;
            if (CharCache.Count > 2000) CharCache.Clear();
            CharCache[key] = result;
            return result;
        }
    }

    /* 글꼴 관리자의 문자 매칭 (Windows · fontconfig). 리눅스 NoDependencies 빌드에서는 null 이 돌아온다 */
    private static SKTypeface? MatchCharacter(SKTypeface primary, int codepoint)
    {
        try
        {
            var m = SKFontManager.Default.MatchCharacter(primary.FamilyName, primary.FontStyle, null, codepoint);
            if (m != null && m.ContainsGlyph(codepoint)) return m;
            m?.Dispose();
        }
        catch
        {
            /* 매칭 실패는 아래의 직접 탐색으로 */
        }
        return null;
    }

    /* 알려진 CJK 글꼴부터, 그 다음 설치된 모든 패밀리를 차례로 뒤져 글리프가 있는 첫 서체 */
    private static readonly string[] CjkPreferred =
    {
        "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Gulim", "Dotum", "Batang",
        "WenQuanYi Zen Hei", "Noto Sans CJK JP", "IPAGothic", "Unifont",
    };

    private static SKTypeface? ScanFamilies(SKTypeface primary, int codepoint)
    {
        try
        {
            var fm = SKFontManager.Default;
            var style = primary.FontStyle;
            IEnumerable<string> names = CjkPreferred.Concat(fm.GetFontFamilies());
            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var tf = fm.MatchFamily(name, style);
                if (tf == null) continue;
                if (tf.ContainsGlyph(codepoint)) return tf;
                tf.Dispose();
            }
        }
        catch
        {
            /* 어떤 서체에도 없으면 주 서체 */
        }
        return null;
    }

    /// <summary>글꼴 선택 목록 (text.js FONTS).</summary>
    public static IReadOnlyList<(string Id, string Name)> Choices { get; } = new (string, string)[]
    {
        ("Arial", "Arial"),
        ("Helvetica", "Helvetica"),
        ("Times New Roman", "Times New Roman"),
        ("Courier New", "Courier New"),
        ("Tahoma", "Tahoma"),
        ("Verdana", "Verdana"),
        ("Calibri", "Calibri"),
        ("Segoe UI", "Segoe UI"),
        ("Malgun Gothic", "맑은 고딕"),
        ("Batang", "바탕"),
        ("Gulim", "굴림"),
        ("Dotum", "돋움"),
    };
}
