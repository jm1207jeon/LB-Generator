// GS1 규칙 — 골든 gtin · parseAIs · bcValidate 와 일치, FNC1 스트림 변환 성질.
using System.Text.Json;
using LaPrint.Core.Barcode;
using Xunit;

namespace LaPrint.Core.Tests.Barcode;

public class Gs1GoldenTests
{
    private static JsonElement Golden(string key) => Fixtures.Golden().RootElement.GetProperty(key);

    [Fact]
    public void Gtin_MatchesGolden()
    {
        var cases = Golden("gtin");
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

    [Fact]
    public void ParseAis_MatchesGolden()
    {
        var cases = Golden("parseAIs");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
        {
            var t = c.GetProperty("t").GetString()!;
            var expected = c.GetProperty("ais");
            var actual = Gs1.ParseAis(t);
            Assert.True(expected.GetArrayLength() == actual.Count, $"\"{t}\": AI 개수 {expected.GetArrayLength()} != {actual.Count}");
            var i = 0;
            foreach (var e in expected.EnumerateArray())
            {
                var (ai, value, def) = actual[i++];
                Assert.Equal(e.GetProperty("ai").GetString(), ai);
                Assert.Equal(e.GetProperty("value").GetString(), value);
                var edef = e.GetProperty("def");
                if (edef.ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(def);
                    continue;
                }
                Assert.NotNull(def);
                Assert.Equal(edef.GetProperty("n").GetString(), def!.Name);
                Assert.Equal(edef.GetProperty("fixed").GetBoolean(), def.Fixed);
                Assert.Equal(edef.TryGetProperty("len", out var len) ? len.GetInt32() : (int?)null, def.Len);
                Assert.Equal(edef.TryGetProperty("max", out var max) ? max.GetInt32() : (int?)null, def.Max);
                Assert.Equal(edef.TryGetProperty("date", out var date) && date.GetBoolean(), def.Date);
            }
        }
    }

    [Fact]
    public void ValidateData_MatchesGolden()
    {
        var cases = Golden("bcValidate");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var c in cases.EnumerateArray())
        {
            var sym = c.GetProperty("sym").GetString()!;
            var data = c.GetProperty("data").GetString()!;
            var expected = c.GetProperty("issues").EnumerateArray()
                .Select(e => (e.GetProperty("level").GetString()!, e.GetProperty("code").GetString()!, e.GetProperty("msg").GetString()!))
                .ToList();
            var actual = Gs1.ValidateData(sym, data).Select(i => (i.Level, i.Code, i.Msg)).ToList();
            Assert.True(expected.SequenceEqual(actual),
                $"{sym} \"{data}\": 기대 [{string.Join("; ", expected)}] 실제 [{string.Join("; ", actual)}]");
        }
    }

    [Theory]
    [InlineData("", "error", "EMPTY", "바코드 데이터가 비어 있습니다.")]
    [InlineData("   ", "error", "EMPTY", "바코드 데이터가 비어 있습니다.")]
    [InlineData("0108806367058034", "warn", "NO_AI", "GS1 심볼로지인데 응용식별자 괄호 표기가 없습니다. 예: (01)08806367087911")]
    [InlineData("(1)abc", "error", "AI_PARSE", "응용식별자를 해석할 수 없습니다. (01)…(10)… 형식인지 확인하세요.")]
    [InlineData("(3103)000123", "warn", "AI_UNKNOWN", "AI (3103)는 사전에 없는 식별자입니다.")]
    [InlineData("(10)", "error", "AI_EMPTY", "AI (10) LOT 값이 비어 있습니다.")]
    [InlineData("(10)123456789012345678901", "error", "AI_LEN", "AI (10) LOT는 최대 20자리입니다. 현재 21자리.")]
    [InlineData("(17)991232", "error", "AI_DATE", "AI (17) 날짜의 일이 올바르지 않습니다: 991232")]
    [InlineData("(01)08806367058035", "error", "GTIN_CHECK", "GTIN 체크디짓이 틀렸습니다: 08806367058035 (올바른 마지막 자리: 4)")]
    public void ValidateData_VerbatimMessages(string data, string level, string code, string msg)
    {
        var issues = Gs1.ValidateData("gs1datamatrix", data);
        Assert.Single(issues);
        Assert.Equal((level, code, msg), (issues[0].Level, issues[0].Code, issues[0].Msg));
    }

    [Fact]
    public void ValidateData_NonGs1SymbologyOnlyChecksEmpty()
    {
        Assert.Empty(Gs1.ValidateData("code128", "(01)bad"));
        Assert.Empty(Gs1.ValidateData("qrcode", "(17)991301"));
        Assert.Single(Gs1.ValidateData("datamatrix", ""));
    }

    [Fact]
    public void ToFnc1Stream_SeparatorOnlyAfterVariableLengthAiWhenAnotherFollows()
    {
        const string GS = "\x1d";
        Assert.Equal("01x" + "10y" + GS + "17z", Gs1.ToFnc1Stream("(01)x(10)y(17)z"));
        // 골든 UDI: 10(가변)→GS, 17(고정)→없음, 240(가변)→GS, 21(마지막)→없음
        Assert.Equal("0108806367058034" + "10LOT1" + GS + "17290531" + "24042-0401" + GS + "213",
            Gs1.ToFnc1Stream("(01)08806367058034(10)LOT1(17)290531(240)42-0401(21)3"));
        // 마지막 가변길이 AI 뒤에는 GS 를 붙이지 않는다
        Assert.Equal("0108806367058034" + "10LOT1", Gs1.ToFnc1Stream("(01)08806367058034(10)LOT1"));
        // 사전에 없는 AI 는 가변길이로 본다
        Assert.Equal("3103000123" + GS + "10L1", Gs1.ToFnc1Stream("(3103)000123(10)L1"));
        Assert.Equal("10L1" + GS + "3103000123", Gs1.ToFnc1Stream("(10)L1(3103)000123"));
        // 고정길이 연속은 구분자 없음
        Assert.Equal("0108806367058034" + "17290531", Gs1.ToFnc1Stream("(01)08806367058034(17)290531"));
        // 괄호 표기가 아니면 그대로
        Assert.Equal("HELLO-123", Gs1.ToFnc1Stream("HELLO-123"));
        Assert.Equal("", Gs1.ToFnc1Stream(""));
    }
}
