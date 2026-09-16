// 파일명 — 골든 'fileName' (금지문자 → '-', {COPY}, 빈 패턴) 과 길이·확장자 규칙.
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using Xunit;

namespace LaPrint.Core.Tests.Export;

public class FileNamingTests
{
    // 골든은 ITEM 42-0401 · LOT LOT1 · DATE 260916 · COPY 2 · REF "" 로 계산했다
    private static Fields GoldenFields() => new()
    {
        ["ITEM"] = "42-0401", ["LOT"] = "LOT1", ["DATE"] = "260916", ["COPY"] = "2", ["REF"] = "",
    };

    [Fact]
    public void Golden_FileName()
    {
        var label = new LabelSize { W = 173.8, H = 75 };
        var cases = Fixtures.Golden().RootElement.GetProperty("fileName");
        Assert.Equal(5, cases.GetArrayLength());
        foreach (var c in cases.EnumerateArray())
        {
            var pat = c.GetProperty("pat").GetString() ?? "";
            Assert.Equal(c.GetProperty("name").GetString(), FileNaming.Build(pat, GoldenFields(), label, null));
        }
    }

    [Fact]
    public void Rules_Length_Extension_Empty()
    {
        var label = new LabelSize { W = 10, H = 10 };
        var f = new Fields { ["ITEM"] = "A" };
        Assert.Equal("label.pdf", FileNaming.Build(" . ", f, label, null));
        Assert.Equal("A.PDF", FileNaming.Build("{ITEM}.PDF", f, label, null));
        Assert.Equal("a b.pdf", FileNaming.Build("a \t\n  b", f, label, null));
        Assert.Equal("ab.pdf", FileNaming.Build("ab", f, label, null));
        var longName = FileNaming.Build(new string('x', 200), f, label, null);
        Assert.Equal(124, longName.Length);
        Assert.EndsWith(".pdf", longName);
        Assert.Equal("{@AB}.pdf", FileNaming.Build("{@AB}", f, label, null));   // 행이 없고 필드도 없으면 토큰 그대로
    }
}
