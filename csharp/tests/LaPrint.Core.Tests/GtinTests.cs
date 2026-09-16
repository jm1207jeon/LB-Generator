// GTIN 체크디짓 — 골든 gtin(g, ok, digit) 와 일치.
using LaPrint.Core.Barcode;
using Xunit;

namespace LaPrint.Core.Tests;

public class GtinTests
{
    [Fact]
    public void Golden_ValidAndCheckDigit()
    {
        var cases = Fixtures.Golden().RootElement.GetProperty("gtin");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
        {
            var g = c.GetProperty("g").GetString()!;
            var ok = c.GetProperty("ok").GetBoolean();
            var digit = c.GetProperty("digit").GetInt32();
            var body = g.Length > 0 ? g[..^1] : "";

            Assert.True(ok == Gs1.GtinValid(g), $"GtinValid(\"{g}\") 는 {ok} 이어야 합니다");
            Assert.True(digit == Gs1.GtinCheckDigit(body), $"GtinCheckDigit(\"{body}\") 는 {digit} 이어야 합니다");
        }
    }

    [Theory]
    [InlineData("08806367058034", true)]
    [InlineData("0880-6367-0580-34", true)]
    [InlineData("08806367058035", false)]
    [InlineData("1234567", false)]
    [InlineData(null, false)]
    public void GtinValid_IgnoresNonDigits(string? g, bool expected) => Assert.Equal(expected, Gs1.GtinValid(g));

    [Fact]
    public void AiTable_HasCoreAis()
    {
        Assert.True(Gs1.AiTable["01"].Fixed);
        Assert.Equal(14, Gs1.AiTable["01"].Len);
        Assert.False(Gs1.AiTable["10"].Fixed);
        Assert.Equal(20, Gs1.AiTable["10"].Max);
        Assert.True(Gs1.AiTable["17"].Date);
        Assert.Equal(30, Gs1.AiTable["240"].Max);
        Assert.Equal(16, Gs1.AiTable.Count);
    }

    [Fact]
    public void Symbologies_ByIdFallsBackToGs1DataMatrix()
    {
        Assert.Equal(5, Symbologies.All.Count);
        Assert.Equal("gs1-128", Symbologies.ById("gs1-128").Id);
        Assert.False(Symbologies.ById("gs1-128").Is2D);
        Assert.Equal("gs1datamatrix", Symbologies.ById("nope").Id);
        Assert.True(Symbologies.ById("qrcode").Is2D);
        Assert.False(Symbologies.ById("qrcode").Gs1);
    }
}
