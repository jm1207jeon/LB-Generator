// 필드 계산 — 골든 fields(123건) 와 문자열 일치, EDATE 말일 클램프, 날짜 형식.
using System.Text.Json;
using LaPrint.Core.Data;
using Xunit;

namespace LaPrint.Core.Tests.Data;

public class FieldComputerTests
{
    [Fact]
    public void Golden_Fields_AllCasesMatch()
    {
        var cases = GoldenData.Golden("fields");
        Assert.Equal(123, cases.GetArrayLength());
        var map = new FieldMap("general");
        var n = 0;
        foreach (var c in cases.EnumerateArray())
        {
            var item = c.GetProperty("item").GetString()!;
            var expected = c.GetProperty("fields");
            var row = GoldenData.RowFromFields(expected, map);
            var inputs = GoldenData.InputsFrom(c.GetProperty("inputs"), item);
            var f = FieldComputer.Compute(row, inputs, map);
            foreach (var p in expected.EnumerateObject())
            {
                if (GoldenData.Volatile.Contains(p.Name)) continue;
                Assert.True(f.ContainsKey(p.Name), $"[{n}] {item}: 필드 {p.Name} 이 없습니다");
                Assert.True(p.Value.GetString() == f[p.Name],
                    $"[{n}] {item} {p.Name}: 기대 \"{p.Value.GetString()}\" 실제 \"{f[p.Name]}\"");
            }
            n++;
        }
    }

    [Fact]
    public void Compute_FillsVolatileAndDates()
    {
        var map = new FieldMap();
        var f = FieldComputer.Compute(null, new JobInputs("X", " L1 ", "", "2026-01-31", 36), map);
        Assert.Equal("X", f["ITEM"]);
        Assert.Equal("L1", f["LOT"]);
        Assert.Equal("", f["GTIN"]);
        Assert.Equal("", f["UPN"]);
        Assert.Equal(new DateTime(2026, 1, 31), f.MfgDate);
        Assert.Equal(new DateTime(2029, 1, 30), f.ExpDate);
        Assert.Equal("290130", f["EXP6"]);
        Assert.Equal("", f["GTIN01"]);
        Assert.Equal("(10)L1", f["UDI_L1"]);
        Assert.Equal("(17)290130(240)X", f["UDI_L2"]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", f["TODAY"]);
        Assert.Matches(@"^\d{2}:\d{2}$", f["NOW"]);
        Assert.Matches(@"^\d{6}$", f["DATE"]);
        Assert.Matches(@"^\d{6}$", f["TIME"]);
        Assert.Equal(map.Cols.Count + 3 + 4 + 4 + 4, f.Count);
    }

    [Fact]
    public void Compute_MonthsZeroMeans36_AndManualExp()
    {
        var map = new FieldMap();
        var auto0 = FieldComputer.Compute(null, new JobInputs("X", "L", "", "2026-06-15", 0), map);
        Assert.Equal("2029-06-14", auto0["EXP"]);

        var manual = FieldComputer.Compute(null, new JobInputs("X", "L", "", "2026-06-15", 0, false, "2030-12-31"), map);
        Assert.Equal("2030-12-31", manual["EXP"]);
        Assert.Equal("301231", manual["EXP6"]);

        var bad = FieldComputer.Compute(null, new JobInputs("X", "L", "", "2026/06/15", 36), map);
        Assert.Equal("", bad["MFG"]);
        Assert.Equal("", bad["EXP"]);
        Assert.Null(bad.MfgDate);
    }

    [Fact]
    public void Golden_Edate_MonthEndClamp()
    {
        var cases = GoldenData.Golden("edate");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
        {
            var mfg = FieldComputer.ParseIso(c.GetProperty("mfg").GetString())!.Value;
            var months = c.GetProperty("months").GetInt32();
            var ed = FieldComputer.Edate(mfg, months);
            Assert.Equal(c.GetProperty("edate").GetString(), FieldComputer.FmtIso(ed));
            var exp = ed.AddDays(-1);
            Assert.Equal(c.GetProperty("exp").GetString(), FieldComputer.FmtIso(exp));
            Assert.Equal(c.GetProperty("exp6").GetString(), FieldComputer.Fmt6(exp));
        }
    }

    [Theory]
    [InlineData("2026-01-31", 1, "2026-02-28")]
    [InlineData("2024-01-31", 1, "2024-02-29")]
    [InlineData("2026-11-30", 3, "2027-02-28")]
    [InlineData("2026-03-31", -1, "2026-02-28")]
    [InlineData("2026-12-15", 13, "2028-01-15")]
    public void Edate_KnownValues(string d, int months, string expected)
        => Assert.Equal(expected, FieldComputer.FmtIso(FieldComputer.Edate(FieldComputer.ParseIso(d)!.Value, months)));

    [Fact]
    public void FormatDate_Patterns()
    {
        var d = new DateTime(2029, 5, 31, 9, 7, 3);
        Assert.Equal("2029-05-31", FieldComputer.FormatDate(d, "YYYY-MM-DD"));
        Assert.Equal("31 MAY 2029", FieldComputer.FormatDate(d, "DD MMM YYYY"));
        Assert.Equal("29.5.31", FieldComputer.FormatDate(d, "YY.M.D"));
        Assert.Equal("09:07:03", FieldComputer.FormatDate(d, "HH:mm:ss"));
        Assert.Equal("", FieldComputer.FormatDate(null, "YYYY"));
    }
}
