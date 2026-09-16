// 색인 — 골든 junk/imgName 와 일치, 정크 건너뛰기·중복 첫 행·검색 가중치·제품명 폴백.
using LaPrint.Core.Data;
using Xunit;

namespace LaPrint.Core.Tests.Data;

public class LabelIndexTests
{
    [Fact]
    public void Golden_Junk()
    {
        var cases = GoldenData.Golden("junk");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
            Assert.True(c.GetProperty("junk").GetBoolean() == LabelIndex.IsJunkKey(c.GetProperty("k").GetString()),
                $"IsJunkKey(\"{c.GetProperty("k").GetString()}\")");
        Assert.True(LabelIndex.IsJunkKey(null));
    }

    [Fact]
    public void Golden_ImgName()
    {
        var cases = GoldenData.Golden("imgName");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
            Assert.True(c.GetProperty("ok").GetBoolean() == LabelIndex.LooksLikeImageName(c.GetProperty("v").GetString()),
                $"LooksLikeImageName(\"{c.GetProperty("v").GetString()}\")");
        Assert.False(LabelIndex.LooksLikeImageName(null));
    }

    private static DbRow Row(string key, string? refv = null, string? product = null, string? img1 = null, string? domestic = null)
    {
        var r = new DbRow { ["H"] = key };
        if (refv is not null) r["L"] = refv;
        if (product is not null) r["AU"] = product;
        if (img1 is not null) r["J"] = img1;
        if (domestic is not null) r["AR"] = domestic;
        return r;
    }

    [Fact]
    public void Build_SkipsJunk_KeepsFirstDuplicate_CountsDups()
    {
        var map = new FieldMap();
        var rows = new[]
        {
            Row("0"), Row(""), Row("-"),
            Row("A-1", "REF-A", "Alpha"),
            Row("A-1", "REF-A2", "Alpha2"),
            Row("B-2", "REF-B", "Beta"),
            Row("B-2"), Row("B-2"),
            Row("c-3", "", "Gamma"),
        };
        var idx = LabelIndex.Build(rows, map);
        Assert.Equal(9, idx.Total);
        Assert.Equal(3, idx.Skipped);
        Assert.Equal(3, idx.ByRef.Count);
        Assert.Equal("REF-A", idx.ByRef["A-1"]["L"]);
        Assert.Equal(new[] { ("B-2", 3), ("A-1", 2) }, idx.Dups);
    }

    [Fact]
    public void Search_ExactPrefixContains_AndLimit()
    {
        var map = new FieldMap();
        var rows = new[]
        {
            Row("16-0401", "BCG-06-040-180", "Biliary"),
            Row("16-0601", "BCG-06-060-180", "Biliary"),
            Row("21-1801", "ECBS-18-100-070", "Esophagus"),
            Row("42-0401", "TNA-06-040-050", "FAUNA"),
            Row("16-04", "X", "Y"),
        };
        var idx = LabelIndex.Build(rows, map);

        var all = idx.Search("");
        Assert.Equal(5, all.Count);
        Assert.Equal("16-0401", all[0].Key);
        Assert.Equal(2, idx.Search("", 2).Count);

        var r = idx.Search("16-04");
        Assert.Equal(new[] { "16-04", "16-0401" }, r.Select(x => x.Key));

        var byRef = idx.Search("ecbs");
        Assert.Equal(new[] { "21-1801" }, byRef.Select(x => x.Key));
        Assert.Equal("ECBS-18-100-070", byRef[0].Ref);
        Assert.Equal("Esophagus", byRef[0].Name);

        var byName = idx.Search("biliary");
        Assert.Equal(new[] { "16-0401", "16-0601" }, byName.Select(x => x.Key));

        var lim = idx.Search("16", 1);
        Assert.Single(lim);
        Assert.Equal("16-0401", lim[0].Key);

        Assert.Empty(idx.Search("zzz"));
        Assert.Empty(LabelIndex.Build(Array.Empty<DbRow>(), map).Search(""));
    }

    [Fact]
    public void Search_RefFallsBackToDomesticThenCatalog()
    {
        var map = new FieldMap();
        var idx = LabelIndex.Build(new[] { Row("K-1", "", "N", null, "DOM-1") }, map);
        Assert.Equal("DOM-1", idx.Search("k-1")[0].Ref);

        var bsc = new FieldMap("bsc");
        var row = new DbRow { ["H"] = "K-2", ["AR"] = "2387", ["J"] = "Name" };
        var idx2 = LabelIndex.Build(new[] { row }, bsc);
        Assert.Equal("2387", idx2.Search("k-2")[0].Ref);
        Assert.Equal("Name", idx2.Search("k-2")[0].Name);
    }

    [Fact]
    public void ProductName_DirectThenImageFallback()
    {
        var map = new FieldMap();
        Assert.Equal("Alpha", LabelIndex.ProductName(Row("k", null, "Alpha", "x.png"), map));
        Assert.Equal("EN name", LabelIndex.ProductName(new DbRow { ["I"] = " EN name " }, map));
        Assert.Equal("HANAROSTENT® Biliary Flap (CCC)", LabelIndex.ProductName(Row("k", null, null, "HANAROSTENT® Biliary Flap (CCC) 1.png"), map));
        Assert.Equal("FAUNASTENT™", LabelIndex.ProductName(Row("k", null, null, "FAUNASTENT™_2.JPG"), map));
        Assert.Equal("BCG", LabelIndex.ProductName(new DbRow { ["O"] = "BCG.png" }, map));
        Assert.Equal("", LabelIndex.ProductName(Row("k", null, null, "그림파일 없음"), map));
        Assert.Equal("", LabelIndex.ProductName(new DbRow(), map));
    }
}
