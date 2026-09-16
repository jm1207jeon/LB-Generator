// 열 매칭 점검 — 표본 추출 간격, 채움 비율 등급, 그림 항목 파일명 비율, 사용 안 함.
using LaPrint.Core.Data;
using Xunit;

namespace LaPrint.Core.Tests.Data;

public class MappingAuditorTests
{
    private static List<DbRow> Rows(int n, Func<int, DbRow> make) => Enumerable.Range(0, n).Select(make).ToList();

    [Fact]
    public void Empty_ReturnsNothing()
    {
        Assert.Empty(MappingAuditor.Audit(Array.Empty<DbRow>(), new FieldMap()));
    }

    [Fact]
    public void Levels_OkWarnErrorOff()
    {
        var map = new FieldMap("general");
        // L(REF): 모두 채움 → ok / AJ(GTIN): 10% → warn / AU(PRODUCT): 비어 있음 → error
        var rows = Rows(10, i => new DbRow
        {
            ["H"] = "K" + i,
            ["L"] = "REF-" + i,
            ["AJ"] = i == 0 ? "1" : "",
            ["O"] = i < 4 ? "BCG.png" : "그림파일 없음",
            ["P"] = i < 6 ? "E.png" : "-",
        });
        var audit = MappingAuditor.Audit(rows, map);
        var byKey = audit.ToDictionary(a => a.Key);

        Assert.Equal(("ok", "", 10, 100), (byKey["REF"].Level, byKey["REF"].Note, byKey["REF"].Filled, byKey["REF"].Pct));
        Assert.Equal("REF-0", byKey["REF"].Sample);
        Assert.Equal(("warn", "표본의 10%에만 값이 있습니다", 1, 10), (byKey["GTIN"].Level, byKey["GTIN"].Note, byKey["GTIN"].Filled, byKey["GTIN"].Pct));
        Assert.Equal(("error", "이 열은 표본 전체가 비어 있습니다", 0, 0), (byKey["PRODUCT"].Level, byKey["PRODUCT"].Note, byKey["PRODUCT"].Filled, byKey["PRODUCT"].Pct));
        Assert.Equal(("error", "그림 항목인데 값의 60%가 파일명이 아닙니다", 40), (byKey["IMG_STENT"].Level, byKey["IMG_STENT"].Note, byKey["IMG_STENT"].ImagePct));
        Assert.Equal(("ok", 60), (byKey["IMG_DELIVERY"].Level, byKey["IMG_DELIVERY"].ImagePct));
        Assert.Equal(("off", "사용 안 함", ""), (byKey["UPN"].Level, byKey["UPN"].Note, byKey["UPN"].Col));
        Assert.Equal("BSC 전용", byKey["UPN"].Group);
        Assert.Equal("BSC UPN", byKey["UPN"].Label);
        Assert.All(audit, a => Assert.Equal(10, a.Total));
    }

    [Fact]
    public void Sampling_StepAndLimit_SampleTruncatedTo40()
    {
        var map = new FieldMap("general");
        var longV = new string('x', 60);
        var rows = Rows(1000, i => new DbRow { ["H"] = "K" + i, ["L"] = i % 2 == 0 ? longV : "" });
        var audit = MappingAuditor.Audit(rows, map, 400);
        var refA = audit.First(a => a.Key == "REF");
        // step = floor(1000/400) = 2 → 0,2,4,… 짝수 행 400개 → 모두 채움
        Assert.Equal(400, refA.Total);
        Assert.Equal(400, refA.Filled);
        Assert.Equal(100, refA.Pct);
        Assert.Equal(40, refA.Sample.Length);

        var small = MappingAuditor.Audit(rows, map, 3);
        Assert.Equal(3, small.First().Total);
    }

    [Fact]
    public void Rounding_HalfUp()
    {
        var map = new FieldMap("general");
        // 8행 중 1행 채움 → 12.5% → JS Math.round = 13
        var rows = Rows(8, i => new DbRow { ["H"] = "K" + i, ["L"] = i == 0 ? "v" : "" });
        var refA = MappingAuditor.Audit(rows, map).First(a => a.Key == "REF");
        Assert.Equal(13, refA.Pct);
        Assert.Equal("warn", refA.Level);
        Assert.Equal("표본의 13%에만 값이 있습니다", refA.Note);
    }
}
