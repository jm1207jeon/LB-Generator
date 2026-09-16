// 바코드 인코더 — bwip-js 골든 PNG 디코드, 자체 인코더 왕복, 심볼로지 식별자, 오류 문구.
using System.Text.Json;
using LaPrint.Core.Barcode;
using Xunit;
using static LaPrint.Core.Tests.Barcode.BarcodeTestUtil;

namespace LaPrint.Core.Tests.Barcode;

public class BarcodeEncoderTests
{
    private static readonly Lazy<List<(string Sym, string Data, int ModulesW)>> GoldenCases = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("barcode_golden.json")));
        return doc.RootElement.EnumerateArray()
            .Select(c => (c.GetProperty("sym").GetString()!, c.GetProperty("data").GetString()!, (int)c.GetProperty("modulesW").GetDouble()))
            .ToList();
    });

    public static IEnumerable<object[]> Cases() => GoldenCases.Value.Select(c => new object[] { c.Sym, c.Data });

    /// <summary>브라우저판(bwip-js) 데이터 문자열을 디코드 결과와 견줄 형태로 — GS1 은 괄호 → FNC1 스트림.</summary>
    private static string Expected(string sym, string data)
        => Symbologies.ById(sym).Gs1 ? Gs1.ToFnc1Stream(data) : data;

    private static string ExpectedSid(string sym) => sym switch
    {
        "gs1datamatrix" => "]d2",
        "gs1-128" => "]C1",
        "datamatrix" => "]d1",
        "code128" => "]C0",
        _ => "]Q1",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void GoldenPng_DecodesToSameData(string sym, string data)
    {
        using var bmp = LoadPng(sym);
        var gs1 = Symbologies.ById(sym).Gs1;
        var r = Decode(bmp, gs1);
        Assert.True(r is not null, $"{sym}.png 를 디코드하지 못했습니다");
        Assert.Equal(ExpectedSid(sym), r!.SymbologyId);
        var text = StripGs1Prefix(r.Text, sym);
        Assert.True(Expected(sym, data) == text, $"{sym}: 기대 '{Show(Expected(sym, data))}' 실제 '{Show(text)}'");
        // GS(FNC1) 위치가 같다
        Assert.Equal(Positions(Expected(sym, data)), Positions(text));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Encode_RoundTripsThroughZXing(string sym, string data)
    {
        var s = BarcodeEncoder.Encode(sym, data);
        Assert.True(s.Error is null, $"{sym}: {s.Error}");
        Assert.NotNull(s.Modules);
        Assert.Equal(s.ModulesW, s.Modules!.GetLength(1));
        Assert.Equal(s.ModulesH, s.Modules.GetLength(0));
        var info = Symbologies.ById(sym);
        if (info.Is2D)
        {
            Assert.Equal(s.ModulesW, s.ModulesH);
            Assert.Equal(1, s.QuietZone);
        }
        else
        {
            Assert.Equal(1, s.ModulesH);
            Assert.Equal(10, s.QuietZone);
        }

        using var bmp = RenderBitmap(s);
        var r = Decode(bmp, info.Gs1);
        Assert.True(r is not null, $"{sym}: 자체 인코딩 결과를 디코드하지 못했습니다");
        Assert.Equal(ExpectedSid(sym), r!.SymbologyId);
        var text = StripGs1Prefix(r.Text, sym);
        Assert.True(Expected(sym, data) == text, $"{sym}: 기대 '{Show(Expected(sym, data))}' 실제 '{Show(text)}'");
    }

    [Fact]
    public void Gs1_128_ModuleWidthMatchesBwipJs()
    {
        // bwip-js 는 1D 좌우에 paddingwidth 2 를 더해 canvas.width 를 재므로 골든 modulesW = 심볼 + 4
        foreach (var (sym, data, modulesW) in GoldenCases.Value.Where(c => !Symbologies.ById(c.Sym).Is2D))
        {
            var s = BarcodeEncoder.Encode(sym, data);
            Assert.True(modulesW - 4 == s.ModulesW, $"{sym}: bwip-js {modulesW - 4} 모듈, ZXing {s.ModulesW} 모듈");
        }
    }

    [Fact]
    public void Gs1_128_SeparatorsAreRealFnc1()
    {
        const string data = "(01)08806367058034(10)LOT1(17)290531(240)42-0401(21)3";
        var s = BarcodeEncoder.Encode("gs1-128", data);
        Assert.Null(s.Error);
        using var bmp = RenderBitmap(s);
        // AssumeGS1 로 읽으면 FNC1 구분자가 GS 로 나온다
        var withGs1 = Decode(bmp, true);
        Assert.NotNull(withGs1);
        Assert.Equal("]C1", withGs1!.SymbologyId);
        Assert.Equal(Gs1.ToFnc1Stream(data), StripGs1Prefix(withGs1.Text, "gs1-128"));
        Assert.Equal(2, Positions(StripGs1Prefix(withGs1.Text, "gs1-128")).Count);
        // AssumeGS1 없이 읽으면 FNC1 은 사라진다 — 제어문자 GS(0x1D)로 부호화된 것이 아니라는 증거
        var plain = Decode(bmp, false);
        Assert.NotNull(plain);
        Assert.DoesNotContain(Gs1.GS, plain!.Text);
        Assert.Equal(Gs1.ToFnc1Stream(data).Replace(Gs1.GS.ToString(), ""), plain.Text);
    }

    [Fact]
    public void Gs1DataMatrix_LeadingFnc1GivesSymbologyD2()
    {
        var s = BarcodeEncoder.Encode("gs1datamatrix", "(01)08806367058034(10)LOT1");
        Assert.Null(s.Error);
        using var bmp = RenderBitmap(s);
        var r = Decode(bmp);
        Assert.NotNull(r);
        Assert.Equal("]d2", r!.SymbologyId);
        // 같은 데이터를 일반 DataMatrix 로 넣으면 ]d1
        var plain = BarcodeEncoder.Encode("datamatrix", "0108806367058034" + "10LOT1");
        using var bmp2 = RenderBitmap(plain);
        var r2 = Decode(bmp2);
        Assert.NotNull(r2);
        Assert.Equal("]d1", r2!.SymbologyId);
    }

    [Fact]
    public void Encode_UnknownSymbologyFallsBackToGs1DataMatrix()
    {
        var a = BarcodeEncoder.Encode("nope", "(01)08806367058034");
        var b = BarcodeEncoder.Encode("gs1datamatrix", "(01)08806367058034");
        Assert.Null(a.Error);
        Assert.Equal(b.ModulesW, a.ModulesW);
        Assert.Equal(b.Modules, a.Modules);
    }

    [Theory]
    [InlineData("gs1datamatrix", "", "바코드 데이터가 비어 있습니다.")]
    [InlineData("code128", "", "바코드 데이터가 비어 있습니다.")]
    [InlineData("qrcode", "", "바코드 데이터가 비어 있습니다.")]
    [InlineData("code128", "한글", "이 심볼로지가 지원하지 않는 문자가 포함되어 있습니다.")]
    [InlineData("datamatrix", "한글", "이 심볼로지가 지원하지 않는 문자가 포함되어 있습니다.")]
    public void Encode_ErrorsAreKorean(string sym, string data, string msg)
    {
        var s = BarcodeEncoder.Encode(sym, data);
        Assert.Null(s.Modules);
        Assert.Equal(0, s.ModulesW);
        Assert.Equal(msg, s.Error);
    }

    [Fact]
    public void Encode_TooLongIsKorean()
    {
        var s = BarcodeEncoder.Encode("qrcode", new string('A', 8000));
        Assert.Null(s.Modules);
        Assert.Equal("데이터가 너무 깁니다.", s.Error);
    }

    [Theory]
    [InlineData("bwipp.gs1datamatrix#123: GS1valueTooShort: AI 01 value too short", "AI (01) 값이 너무 짧습니다.")]
    [InlineData("GS1valueTooLong AI 10", "AI (10) 값이 너무 깁니다.")]
    [InlineData("GS1aiMissingOpenParen", "GS1 데이터는 (01) 같은 괄호 표기로 시작해야 합니다.")]
    [InlineData("bwipp.gs1badAI", "알 수 없는 응용식별자(AI)입니다.")]
    [InlineData("Bad character in input: ASCII value=54620", "이 심볼로지가 지원하지 않는 문자가 포함되어 있습니다.")]
    [InlineData("Message contains characters outside iso-8859-1 encoding.", "이 심볼로지가 지원하지 않는 문자가 포함되어 있습니다.")]
    [InlineData("Found empty contents", "바코드 데이터가 비어 있습니다.")]
    [InlineData("unknown encoder: foo", "지원하지 않는 바코드 종류입니다.")]
    [InlineData("Contents length should be between 1 and 80 characters, but got 100", "데이터가 너무 깁니다.")]
    [InlineData("Data too big", "데이터가 너무 깁니다.")]
    [InlineData("bwipp.code128#42: something else", "something else")]
    [InlineData("plain message", "plain message")]
    public void HumanizeError_Mapping(string raw, string expected)
        => Assert.Equal(expected, BarcodeEncoder.HumanizeError(raw));

    private static List<int> Positions(string s)
    {
        var list = new List<int>();
        for (var i = 0; i < s.Length; i++) if (s[i] == Gs1.GS) list.Add(i);
        return list;
    }
}
