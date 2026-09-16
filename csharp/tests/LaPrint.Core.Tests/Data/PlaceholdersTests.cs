// 플레이스홀더 — 골든 resolve(42-0401, LOT1/3/2026-06-01/36개월) 와 일치, 자동완성 목록 순서.
using LaPrint.Core.Data;
using Xunit;

namespace LaPrint.Core.Tests.Data;

public class PlaceholdersTests
{
    private static (Fields F, DbRow Row) Item42()
    {
        var map = new FieldMap("general");
        var row = GoldenData.RowOf("42-0401", map);
        Assert.NotNull(row);
        var f = FieldComputer.Compute(row, new JobInputs("42-0401", "LOT1", "3", "2026-06-01", 36, true), map);
        return (f, row!);
    }

    [Fact]
    public void Golden_Resolve_AndUnresolved()
    {
        var (f, row) = Item42();
        var cases = GoldenData.Golden("resolve");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
        {
            var text = c.GetProperty("text").GetString()!;
            var expected = c.GetProperty("result").GetString()!;
            var unresolved = c.GetProperty("unresolved").EnumerateArray().Select(u => u.GetString()!).ToList();
            Assert.True(expected == Placeholders.Resolve(text, f, row), $"Resolve(\"{text}\")");
            Assert.Equal(unresolved, Placeholders.Unresolved(text, f, row));
        }
    }

    [Fact]
    public void Resolve_FallbackSemantics()
    {
        var (f, row) = Item42();
        Assert.Equal("", Placeholders.Resolve("{REF}", f, row));
        Assert.Equal("없음", Placeholders.Resolve("{REF|없음}", f, row));
        Assert.Equal("", Placeholders.Resolve("{REF|}", f, row));
        Assert.Equal("{NOPE}", Placeholders.Resolve("{NOPE}", f, row));
        Assert.Equal("대체", Placeholders.Resolve("{NOPE|대체}", f, row));
        Assert.Equal("{@ZZ}", Placeholders.Resolve("{@ZZ}", f, row));
        Assert.Equal("{@ZZ}", Placeholders.Resolve("{@ZZ}", f, null));
        Assert.Equal("-", Placeholders.Resolve("{@ZZ|-}", f, null));
        Assert.Equal("", Placeholders.Resolve(null!, f, row));
        Assert.Equal("{ITEM", Placeholders.Resolve("{ITEM", f, row));
        // 날짜 키가 아니면 :형식 은 무시되고 필드 값("")이 쓰인다
        Assert.Equal("", Placeholders.Resolve("{REF:YYYY}", f, row));
        Assert.Equal("42-0401", Placeholders.Resolve("{ITEM:YYYY}", f, row));
    }

    [Fact]
    public void Resolve_DateFormats_TodayAndMissing()
    {
        var map = new FieldMap();
        var f = FieldComputer.Compute(null, new JobInputs("X", "L", "", "", 36), map);
        Assert.Equal("", Placeholders.Resolve("{MFG:YYYY}", f, null));
        Assert.Equal("없음", Placeholders.Resolve("{EXP:YYYY|없음}", f, null));
        var today = Placeholders.Resolve("{TODAY:YYYY-MM-DD}", f, null);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", today);
        Assert.Equal(f["TODAY"], today);
    }

    [Fact]
    public void Golden_PlaceholderList_OrderAndLabels()
    {
        var expected = GoldenData.Golden("placeholders").EnumerateArray()
            .Select(p => (p.GetProperty("key").GetString()!, p.GetProperty("token").GetString()!, p.GetProperty("label").GetString()!))
            .ToList();
        var actual = Placeholders.List(new FieldMap("general"));
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Item1, actual[i].Key);
            Assert.Equal(expected[i].Item2, actual[i].Token);
            Assert.Equal(expected[i].Item3, actual[i].Label);
        }
        Assert.Equal(60, actual.Count);
        Assert.Equal("ITEM", actual[0].Key);
        Assert.Equal("TIME", actual[^1].Key);
        Assert.Single(actual, p => p.Key == "GTIN");
    }
}
