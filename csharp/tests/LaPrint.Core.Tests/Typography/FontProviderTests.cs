// 글꼴 공급 — 설치 여부 감지, 폴백 사슬, CJK 대체 서체, 선택 목록.
using LaPrint.Core.Typography;
using Xunit;

namespace LaPrint.Core.Tests.Typography;

public class FontProviderTests
{
    [Fact]
    public void IsAvailable_DetectsInstalledFont()
    {
        var font = TextLayoutEngineTests.TestFont;
        Assert.True(FontProvider.IsAvailable(font));
        Assert.False(FontProvider.IsAvailable("NoSuchFont-LaPrint-12345"));
        Assert.False(FontProvider.IsAvailable(""));
    }

    [Fact]
    public void Get_ReturnsRequestedFamilyAndCaches()
    {
        var font = TextLayoutEngineTests.TestFont;
        var a = FontProvider.Get(font, false, false);
        var b = FontProvider.Get(font, false, false);
        Assert.Same(a, b);
        Assert.Equal(font, a.FamilyName, ignoreCase: true);
        var bold = FontProvider.Get(font, true, false);
        Assert.True(bold.IsBold);
    }

    [Fact]
    public void Get_FallsBackWhenMissing()
    {
        var tf = FontProvider.Get("NoSuchFont-LaPrint-12345", false, false);
        Assert.NotNull(tf);
        Assert.True(tf.ContainsGlyph('A'));
        Assert.NotNull(FontProvider.Get("", false, true));
    }

    [Fact]
    public void CjkFallback_FindsFaceWithHangul()
    {
        var primary = FontProvider.Get(TextLayoutEngineTests.TestFont, false, false);
        var fb = FontProvider.CjkFallback(primary, "AB 한글");
        Assert.True(fb.ContainsGlyph('한'));
        Assert.Same(primary, FontProvider.CjkFallback(primary, "ABC 123"));
        Assert.Same(primary, FontProvider.CjkFallback(primary, ""));
    }

    [Fact]
    public void Choices_MatchTextJsFonts()
    {
        Assert.Equal(12, FontProvider.Choices.Count);
        Assert.Equal(("Arial", "Arial"), FontProvider.Choices[0]);
        Assert.Equal(("Malgun Gothic", "맑은 고딕"), FontProvider.Choices[8]);
        Assert.Equal(("Dotum", "돋움"), FontProvider.Choices[11]);
    }
}
