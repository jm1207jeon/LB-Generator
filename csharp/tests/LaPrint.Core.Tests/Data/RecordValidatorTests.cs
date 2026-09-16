// 검증 — 골든 validate(기준일 2026-09-15) 와 level+code 집합 일치, 규칙 끄기, GTIN 체크디짓, LOT 힌트.
using LaPrint.Core.Data;
using Xunit;

namespace LaPrint.Core.Tests.Data;

public class RecordValidatorTests
{
    private static readonly DateTime GoldenToday = new(2026, 9, 15);

    [Fact]
    public void Golden_Validate_LevelCodeSetsMatch()
    {
        var map = new FieldMap("general");
        var row42 = GoldenData.RowOf("42-0401", map);
        Assert.NotNull(row42);
        var cases = GoldenData.Golden("validate");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
        {
            var inputs = GoldenData.InputsFrom(c.GetProperty("inputs"));
            var row = inputs.Item == "42-0401" ? row42 : null;
            var f = FieldComputer.Compute(row, inputs, map);
            var issues = RecordValidator.Validate(f, row, new ValidationRules(), GoldenToday);

            var expected = c.GetProperty("issues").EnumerateArray()
                .Select(i => (i.GetProperty("level").GetString()!, i.GetProperty("code").GetString()!,
                              i.GetProperty("msg").GetString()!,
                              i.TryGetProperty("field", out var fd) && fd.ValueKind == System.Text.Json.JsonValueKind.String ? fd.GetString() : null))
                .ToList();
            var actual = issues.Select(i => (i.Level, i.Code, i.Msg, i.Field)).ToList();
            Assert.Equal(expected.Select(e => e.Item1 + ":" + e.Item2).ToHashSet(), actual.Select(a => a.Level + ":" + a.Code).ToHashSet());
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Validate_RulesCanBeTurnedOff()
    {
        var map = new FieldMap();
        var f = FieldComputer.Compute(null, new JobInputs("", "", "", "", 36), map);
        var off = new ValidationRules { RequireItem = false, RequireLot = false, RequireMfg = false };
        Assert.Empty(RecordValidator.Validate(f, null, off));
        var sn = new ValidationRules { RequireItem = false, RequireLot = false, RequireMfg = false, RequireSn = true };
        Assert.Contains(RecordValidator.Validate(f, null, sn), i => i.Code == "NO_SN" && i.Field == "sn");
    }

    [Fact]
    public void Validate_RowChecks_GtinRefProduct()
    {
        var map = new FieldMap();
        var row = new DbRow { ["H"] = "X", ["AJ"] = "08806367058035" };
        var f = FieldComputer.Compute(row, new JobInputs("X", "L", "", "2026-01-01", 36), map);
        var issues = RecordValidator.Validate(f, row, new ValidationRules(), GoldenToday);
        var gtin = Assert.Single(issues, i => i.Code == "GTIN_CHECK");
        Assert.Equal("error", gtin.Level);
        Assert.Equal("GTIN 체크디짓이 틀렸습니다: 08806367058035 (올바른 마지막 자리: 4)", gtin.Msg);
        Assert.Contains(issues, i => i.Code == "NO_REF" && i.Level == "warn");
        Assert.Contains(issues, i => i.Code == "NO_PRODUCT" && i.Level == "warn");

        var noGtin = new DbRow { ["H"] = "X" };
        var f2 = FieldComputer.Compute(noGtin, new JobInputs("X", "L", "", "2026-01-01", 36), map);
        Assert.Contains(RecordValidator.Validate(f2, noGtin, new ValidationRules { CheckGtin = false }), i => i.Code == "NO_GTIN");
    }

    [Fact]
    public void Validate_ExpPast_DependsOnToday()
    {
        var map = new FieldMap();
        var f = FieldComputer.Compute(null, new JobInputs("", "L", "", "2020-01-01", 12), map);
        var rules = new ValidationRules { RequireItem = false };
        Assert.Contains(RecordValidator.Validate(f, null, rules, new DateTime(2021, 6, 1)), i => i.Code == "EXP_PAST");
        Assert.DoesNotContain(RecordValidator.Validate(f, null, rules, new DateTime(2020, 12, 31)), i => i.Code == "EXP_PAST");
        Assert.DoesNotContain(RecordValidator.Validate(f, null, rules, new DateTime(2020, 12, 30)), i => i.Code == "EXP_PAST");
        Assert.DoesNotContain(RecordValidator.Validate(f, null, new ValidationRules { RequireItem = false, CheckExpPast = false }, new DateTime(2025, 1, 1)), i => i.Code == "EXP_PAST");
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("L2604A", null)]
    [InlineData("A-1_b", null)]
    [InlineData("A 1", "LOT에 공백이 들어 있습니다.")]
    [InlineData("A#1", "LOT에 특수문자가 들어 있습니다. 바코드 판독에 문제가 될 수 있습니다.")]
    [InlineData("가나", "LOT에 특수문자가 들어 있습니다. 바코드 판독에 문제가 될 수 있습니다.")]
    public void LotHint_Cases(string lot, string? expected) => Assert.Equal(expected, RecordValidator.LotHint(lot));
}
