// GS1 유틸 — GTIN 체크디짓, AI 사전, 괄호 표기 파싱·검증, FNC1 스트림 변환 (barcode.js).
using LaPrint.Core.Data;

namespace LaPrint.Core.Barcode;

/// <summary>GS1 응용식별자 정의. Fixed 가 아니면 값 뒤에 FNC1(GS) 구분자가 필요하다.</summary>
public sealed record AiDef(string Name, int? Len, int? Max, bool Fixed, bool Date = false);

/// <summary>GS1 데이터 규칙.</summary>
public static class Gs1
{
    /// <summary>FNC1 구분자로 쓰는 GS 문자.</summary>
    public const char GS = '\x1d';

    /// <summary>GTIN-8/12/13/14 체크디짓 검증. 숫자가 아닌 문자는 무시한다.</summary>
    public static bool GtinValid(string? gtin)
    {
        var g = Digits(gtin);
        if (g.Length is not (8 or 12 or 13 or 14)) return false;
        var chk = g[^1] - '0';
        return GtinCheckDigit(g[..^1]) == chk;
    }

    /// <summary>본문(체크디짓 제외)으로부터 체크디짓 계산. 오른쪽부터 3,1,3,1… 가중치.</summary>
    public static int GtinCheckDigit(string? body)
    {
        var b = Digits(body);
        var sum = 0;
        for (var i = 0; i < b.Length; i++)
        {
            var fromRight = b.Length - 1 - i;
            sum += (b[i] - '0') * (fromRight % 2 == 0 ? 3 : 1);
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>AI 사전 — 고정길이 여부가 FNC1 구분자 필요성을 결정한다.</summary>
    public static IReadOnlyDictionary<string, AiDef> AiTable { get; } = new Dictionary<string, AiDef>
    {
        ["00"] = new("SSCC", 18, null, true),
        ["01"] = new("GTIN", 14, null, true),
        ["02"] = new("포함 GTIN", 14, null, true),
        ["10"] = new("LOT", null, 20, false),
        ["11"] = new("제조일 YYMMDD", 6, null, true, true),
        ["12"] = new("지급기일", 6, null, true, true),
        ["13"] = new("포장일", 6, null, true, true),
        ["15"] = new("품질유지기한", 6, null, true, true),
        ["16"] = new("판매기한", 6, null, true, true),
        ["17"] = new("사용기한 YYMMDD", 6, null, true, true),
        ["20"] = new("변형", 2, null, true),
        ["21"] = new("SN(일련번호)", null, 20, false),
        ["30"] = new("수량", null, 8, false),
        ["240"] = new("추가 품목식별", null, 30, false),
        ["241"] = new("고객 품번", null, 30, false),
        ["10x"] = new("기타", null, 30, false),
    };

    /// <summary>"(01)0880…(10)26041086" → (AI, 값, 정의) 목록.</summary>
    public static IReadOnlyList<(string Ai, string Value, AiDef? Def)> ParseAis(string text)
        => throw new NotImplementedException("Gs1.ParseAis — 아직 구현되지 않았습니다");

    /// <summary>바코드 데이터 사전 검증 (barcode.js validateData 규칙 그대로).</summary>
    public static IReadOnlyList<Issue> ValidateData(string symbology, string text)
        => throw new NotImplementedException("Gs1.ValidateData — 아직 구현되지 않았습니다");

    /// <summary>"(01)x(10)y(17)z" → "01x" + "10y" + GS + "17z". 가변길이 AI 뒤에만 GS, 마지막은 생략.</summary>
    public static string ToFnc1Stream(string bracketed)
        => throw new NotImplementedException("Gs1.ToFnc1Stream — 아직 구현되지 않았습니다");

    private static string Digits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            if (ch is >= '0' and <= '9') sb.Append(ch);
        return sb.ToString();
    }
}
