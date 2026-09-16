// 열 매핑 — 골든 fieldCols(def/bsc/labels/groups/imageFields) 와 일치, 프로필·사용자 매핑 적용 규칙.
using System.Text.Json;
using LaPrint.Core.Data;
using Xunit;

namespace LaPrint.Core.Tests;

public class FieldMapTests
{
    private static JsonElement FieldCols => Fixtures.Golden().RootElement.GetProperty("fieldCols");

    private static Dictionary<string, string> ToDict(JsonElement e)
        => e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");

    [Fact]
    public void DefaultCols_MatchGolden()
    {
        var expected = ToDict(FieldCols.GetProperty("def"));
        Assert.Equal(expected, new Dictionary<string, string>(FieldMap.DefaultCols));
        Assert.Equal("", FieldMap.DefaultCols["UPN"]);
        Assert.Equal("", FieldMap.DefaultCols["REF_JP"]);
    }

    [Fact]
    public void BscCols_MatchGolden()
    {
        var expected = ToDict(FieldCols.GetProperty("bsc"));
        Assert.Equal(expected, new Dictionary<string, string>(FieldMap.BscCols));
    }

    [Fact]
    public void Labels_MatchGolden()
    {
        var expected = ToDict(FieldCols.GetProperty("labels"));
        Assert.Equal(expected, new Dictionary<string, string>(FieldMap.Labels));
    }

    [Fact]
    public void Groups_MatchGolden()
    {
        var expected = FieldCols.GetProperty("groups").EnumerateArray()
            .Select(g => (g.GetProperty("g").GetString()!, g.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToArray()))
            .ToList();
        Assert.Equal(expected.Count, FieldMap.Groups.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Item1, FieldMap.Groups[i].Group);
            Assert.Equal(expected[i].Item2, FieldMap.Groups[i].Keys);
        }
    }

    [Fact]
    public void ImageFields_MatchGolden()
    {
        var expected = FieldCols.GetProperty("imageFields").EnumerateArray().Select(k => k.GetString()!).ToArray();
        Assert.Equal(expected, FieldMap.ImageFields);
    }

    [Fact]
    public void Profiles_GeneralAndBsc()
    {
        Assert.Equal(2, Profiles.All.Count);
        Assert.Equal("general", Profiles.Get("general").Key);
        Assert.Equal("bsc", Profiles.Get("bsc").Key);
        Assert.Equal("general", Profiles.Get("nope").Key);
        Assert.Equal("01.라벨출력DB", Profiles.Get("general").DefaultFile);
        Assert.Equal("02.라벨출력DB_BSC", Profiles.Get("bsc").DefaultFile);
        Assert.Empty(Profiles.Get("general").Map);
        Assert.Same(FieldMap.BscCols, Profiles.Get("bsc").Map);
    }

    [Fact]
    public void BaseCols_OverlaysProfileMap()
    {
        var m = new FieldMap();
        Assert.Equal("general", m.Profile);
        Assert.Equal("H", m.KeyCol);
        Assert.Equal(FieldMap.DefaultCols, m.Cols);

        m.SetProfile("bsc");
        Assert.Equal("bsc", m.Profile);
        Assert.Equal("J", m.Cols["PRODUCT"]);
        Assert.Equal("AQ", m.Cols["UPN"]);
        Assert.Equal("", m.Cols["IMG_NAME1"]);
        Assert.Equal("L", m.Cols["REF"]);
        Assert.Equal(FieldMap.DefaultCols.Count, m.Cols.Count);
    }

    [Fact]
    public void Apply_TrimsUppercasesAndIgnoresUnknownKeys()
    {
        var m = new FieldMap();
        m.Apply(new Dictionary<string, string> { ["REF"] = " m ", ["GTIN"] = "", ["NOT_A_FIELD"] = "Z" });

        Assert.Equal("M", m.Cols["REF"]);
        Assert.Equal("", m.Cols["GTIN"]);
        Assert.False(m.Cols.ContainsKey("NOT_A_FIELD"));
        Assert.Equal(new Dictionary<string, string> { ["REF"] = "M", ["GTIN"] = "" }, m.Diff());

        m.Reset();
        Assert.Empty(m.Diff());
        Assert.Equal("AJ", m.Cols["GTIN"]);
    }

    [Fact]
    public void SetProfile_UnknownFallsBackToGeneral()
    {
        var m = new FieldMap("bsc");
        m.SetProfile("???");
        Assert.Equal("general", m.Profile);
        Assert.Equal("AU", m.Cols["PRODUCT"]);
    }
}
