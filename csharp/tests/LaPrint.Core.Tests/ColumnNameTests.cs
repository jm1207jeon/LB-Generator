// 열 문자 — 골든 cols(i → name → back) 와 일치, 대소문자 무시, 잘못된 입력은 -1.
using LaPrint.Core.Data;
using Xunit;

namespace LaPrint.Core.Tests;

public class ColumnNameTests
{
    [Fact]
    public void Golden_RoundTrip()
    {
        var cols = Fixtures.Golden().RootElement.GetProperty("cols");
        Assert.True(cols.GetArrayLength() > 0);
        foreach (var c in cols.EnumerateArray())
        {
            var i = c.GetProperty("i").GetInt32();
            var name = c.GetProperty("name").GetString()!;
            var back = c.GetProperty("back").GetInt32();
            Assert.Equal(name, ColumnName.FromIndex(i));
            Assert.Equal(back, ColumnName.ToIndex(name));
        }
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void FromIndex_KnownValues(int i, string expected) => Assert.Equal(expected, ColumnName.FromIndex(i));

    [Theory]
    [InlineData("a", 0)]
    [InlineData(" aa ", 26)]
    [InlineData("Bc", 54)]
    [InlineData("", -1)]
    [InlineData(null, -1)]
    [InlineData("A1", -1)]
    [InlineData("1", -1)]
    public void ToIndex_CaseInsensitiveAndInvalid(string? name, int expected) => Assert.Equal(expected, ColumnName.ToIndex(name));

    [Fact]
    public void FromIndex_Negative_IsEmpty() => Assert.Equal("", ColumnName.FromIndex(-1));
}
