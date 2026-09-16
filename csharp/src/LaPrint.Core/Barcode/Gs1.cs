// GS1 유틸 — GTIN 체크디짓, AI 사전, 괄호 표기 파싱·검증, FNC1 스트림 변환 (barcode.js).
using System.Text;
using System.Text.RegularExpressions;
using LaPrint.Core.Data;

namespace LaPrint.Core.Barcode;

/// <summary>GS1 응용식별자 정의. Fixed 가 아니면 값 뒤에 FNC1(GS) 구분자가 필요하다.</summary>
public sealed record AiDef(string Name, int? Len, int? Max, bool Fixed, bool Date = false);

/// <summary>GS1 데이터 규칙.</summary>
public static partial class Gs1
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

    [GeneratedRegex(@"\((\d{2,4})\)([^(]*)")]
    private static partial Regex AiRegex();

    // GS1 일반사양 §7.8.4 — 앞 두 자리가 이 값이면 길이가 미리 정해진 AI 라서 뒤에 FNC1 구분자를 넣지 않는다
    private static readonly HashSet<string> PredefinedLengthPrefixes = new()
    {
        "00", "01", "02", "03", "04", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
        "31", "32", "33", "34", "35", "36", "41",
    };

    /// <summary>사전에 없는 AI 라도 GS1 이 길이를 미리 정해 둔 AI(앞 두 자리 기준)면 구분자가 필요 없다.</summary>
    public static bool IsPredefinedLength(string ai)
        => !string.IsNullOrEmpty(ai) && ai.Length >= 2 && PredefinedLengthPrefixes.Contains(ai[..2]);

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex SixDigits();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex AllDigits();

    /// <summary>"(01)0880…(10)26041086" → (AI, 값, 정의) 목록.</summary>
    public static IReadOnlyList<(string Ai, string Value, AiDef? Def)> ParseAis(string text)
    {
        var outList = new List<(string Ai, string Value, AiDef? Def)>();
        foreach (Match m in AiRegex().Matches(text ?? ""))
        {
            var ai = m.Groups[1].Value;
            outList.Add((ai, m.Groups[2].Value, AiTable.TryGetValue(ai, out var def) ? def : null));
        }
        return outList;
    }

    /// <summary>바코드 데이터 사전 검증 (barcode.js validateData 규칙 그대로).</summary>
    public static IReadOnlyList<Issue> ValidateData(string symbology, string text)
    {
        var sym = Symbologies.ById(symbology);
        var issues = new List<Issue>();
        var s = text ?? "";
        if (string.IsNullOrWhiteSpace(s))
        {
            issues.Add(new Issue("error", "EMPTY", "바코드 데이터가 비어 있습니다."));
            return issues;
        }
        if (!sym.Gs1) return issues;

        if (s.IndexOf('(') < 0)
        {
            issues.Add(new Issue("warn", "NO_AI", "GS1 심볼로지인데 응용식별자 괄호 표기가 없습니다. 예: (01)08806367087911"));
            return issues;
        }
        var ais = ParseAis(s);
        if (ais.Count == 0)
        {
            issues.Add(new Issue("error", "AI_PARSE", "응용식별자를 해석할 수 없습니다. (01)…(10)… 형식인지 확인하세요."));
            return issues;
        }
        foreach (var a in ais)
        {
            var d = a.Def;
            if (d is null) { issues.Add(new Issue("warn", "AI_UNKNOWN", $"AI ({a.Ai})는 사전에 없는 식별자입니다.")); continue; }
            if (a.Value.Length == 0) { issues.Add(new Issue("error", "AI_EMPTY", $"AI ({a.Ai}) {d.Name} 값이 비어 있습니다.")); continue; }
            if (d.Fixed && d.Len is > 0 && a.Value.Length != d.Len)
                issues.Add(new Issue("error", "AI_LEN", $"AI ({a.Ai}) {d.Name}는 정확히 {d.Len}자리여야 합니다. 현재 {a.Value.Length}자리."));
            if (!d.Fixed && d.Max is > 0 && a.Value.Length > d.Max)
                issues.Add(new Issue("error", "AI_LEN", $"AI ({a.Ai}) {d.Name}는 최대 {d.Max}자리입니다. 현재 {a.Value.Length}자리."));
            if (d.Date && SixDigits().IsMatch(a.Value))
            {
                var mo = int.Parse(a.Value.Substring(2, 2));
                var dy = int.Parse(a.Value.Substring(4, 2));
                if (mo < 1 || mo > 12) issues.Add(new Issue("error", "AI_DATE", $"AI ({a.Ai}) 날짜의 월이 올바르지 않습니다: {a.Value}"));
                else if (dy > 31) issues.Add(new Issue("error", "AI_DATE", $"AI ({a.Ai}) 날짜의 일이 올바르지 않습니다: {a.Value}"));
            }
            if (a.Ai == "01" && AllDigits().IsMatch(a.Value) && !GtinValid(a.Value))
                issues.Add(new Issue("error", "GTIN_CHECK",
                    $"GTIN 체크디짓이 틀렸습니다: {a.Value} (올바른 마지막 자리: {GtinCheckDigit(a.Value[..^1])})"));
        }
        return issues;
    }

    /// <summary>"(01)x(10)y(17)z" → "01x" + "10y" + GS + "17z". 가변길이 AI 뒤에만 GS, 마지막은 생략.</summary>
    public static string ToFnc1Stream(string bracketed)
    {
        var s = bracketed ?? "";
        var ais = ParseAis(s);
        // 괄호 표기가 아니면 손대지 않는다 (검증 단계에서 NO_AI 로 알린다)
        if (ais.Count == 0) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < ais.Count; i++)
        {
            var (ai, value, def) = ais[i];
            sb.Append(ai).Append(value);
            // 사전에 있으면 그 정의를, 없으면 GS1 의 고정길이 접두 목록을 따른다 (bwip-js/BWIPP 와 같은 결과)
            var variable = def is null ? !IsPredefinedLength(ai) : !def.Fixed;
            if (variable && i < ais.Count - 1) sb.Append(GS);
        }
        return sb.ToString();
    }

    private static string Digits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (ch is >= '0' and <= '9') sb.Append(ch);
        return sb.ToString();
    }
}
