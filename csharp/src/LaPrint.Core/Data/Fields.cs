// 작업 입력과 계산된 필드 — 엑셀 원본(PML-001 Rev.1) 수식과 1:1 (data.js computeFields).
//   EXP       = EDATE(MFG, 개월) - 1일
//   EXP6      = TEXT(EXP, "YYMMDD")
//   UDI       = "(01)"&GTIN & "(10)"&LOT & "(17)"&EXP6 & "(240)"&품목번호 & "(21)"&SN
using System.Globalization;
using System.Text.RegularExpressions;

namespace LaPrint.Core.Data;

/// <summary>작업 입력. Mfg 는 ISO 날짜 문자열, Months 는 유효기간(개월).</summary>
public sealed record JobInputs(string Item, string Lot, string Sn, string Mfg, int Months = 36,
                               bool ExpAuto = true, string Exp = "");

/// <summary>계산된 필드 키 → 문자열. 날짜는 파싱된 값도 함께 가진다.</summary>
public sealed class Fields : Dictionary<string, string>
{
    public DateTime? MfgDate { get; set; }
    public DateTime? ExpDate { get; set; }

    /// <summary>필드 값. 없는 키는 "".</summary>
    public string Get(string key)
        => TryGetValue(key, out var v) ? v : "";
}

/// <summary>필드 계산 — ITEM/LOT/SN + 모든 열 + MFG/MFG6/EXP/EXP6 + UDI + TODAY/NOW/DATE/TIME.</summary>
public static class FieldComputer
{
    private static readonly string[] MonthsEn = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
    private static readonly Regex IsoRe = new(@"^(\d{4})-(\d{2})-(\d{2})$", RegexOptions.Compiled);
    private static readonly Regex PatternRe = new("YYYY|YY|MMM|MM|M|DD|D|HH|mm|ss", RegexOptions.Compiled);

    /// <summary>한 건의 라벨에 들어갈 모든 값을 계산한다. row 가 없으면 열 값은 모두 "".</summary>
    public static Fields Compute(DbRow? row, JobInputs inputs, FieldMap map)
    {
        var f = new Fields
        {
            ["ITEM"] = (inputs.Item ?? "").Trim(),
            ["LOT"] = (inputs.Lot ?? "").Trim(),
            ["SN"] = (inputs.Sn ?? "").Trim(),
        };
        foreach (var kv in map.Cols)
            f[kv.Key] = row is null ? "" : row.Get(kv.Value).Trim();

        // 엑셀에서 '-' 는 "해당 없음"을 뜻한다. 표시용으로는 그대로 둔다.
        var mfg = ParseIso(inputs.Mfg);
        DateTime? exp = null;
        if (inputs.ExpAuto)
        {
            // JS: Number(inputs.months) || 36 — 0 이면 36
            if (mfg is not null) exp = Edate(mfg.Value, inputs.Months == 0 ? 36 : inputs.Months).AddDays(-1);
        }
        else
        {
            exp = ParseIso(inputs.Exp);
        }
        f["MFG"] = FmtIso(mfg); f["MFG6"] = Fmt6(mfg);
        f["EXP"] = FmtIso(exp); f["EXP6"] = Fmt6(exp);
        f.MfgDate = mfg; f.ExpDate = exp;

        var now = DateTime.Now;
        f["TODAY"] = FmtIso(now);
        f["NOW"] = $"{P2(now.Hour)}:{P2(now.Minute)}";
        f["DATE"] = Fmt6(now);
        f["TIME"] = $"{P2(now.Hour)}{P2(now.Minute)}{P2(now.Second)}";

        // UDI — 값이 없는 AI 그룹은 통째로 생략한다
        static string G(string ai, string v) => v.Length > 0 ? $"({ai}){v}" : "";
        f["GTIN01"] = G("01", f.Get("GTIN"));
        f["UDI_L1"] = f["GTIN01"] + G("10", f["LOT"]);
        f["UDI_L2"] = G("17", f["EXP6"]) + G("240", f["ITEM"]) + G("21", f["SN"]);
        f["UDI_FULL"] = f["UDI_L1"] + f["UDI_L2"];
        return f;
    }

    /// <summary>엑셀 EDATE (말일 클램프).</summary>
    public static DateTime Edate(DateTime d, int months)
    {
        var total = d.Year * 12 + (d.Month - 1) + months;
        var y = (int)Math.Floor(total / 12.0);
        var m = total - y * 12 + 1;
        var last = DateTime.DaysInMonth(y, m);
        return new DateTime(y, m, Math.Min(d.Day, last));
    }

    /// <summary>YYYY YY MMM MM M DD D HH mm ss 패턴. d 가 없으면 "".</summary>
    public static string FormatDate(DateTime? d, string pattern)
    {
        if (d is null) return "";
        var v = d.Value;
        return PatternRe.Replace(pattern ?? "", mt => mt.Value switch
        {
            "YYYY" => v.Year.ToString(CultureInfo.InvariantCulture),
            "YY" => Yy(v),
            "MMM" => MonthsEn[v.Month - 1],
            "MM" => P2(v.Month),
            "M" => v.Month.ToString(CultureInfo.InvariantCulture),
            "DD" => P2(v.Day),
            "D" => v.Day.ToString(CultureInfo.InvariantCulture),
            "HH" => P2(v.Hour),
            "mm" => P2(v.Minute),
            "ss" => P2(v.Second),
            _ => mt.Value,
        });
    }

    /// <summary>"yyyy-MM-dd" 만 받는다. 아니면 null.</summary>
    public static DateTime? ParseIso(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var m = IsoRe.Match(s);
        if (!m.Success) return null;
        var y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var d = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        // JS 의 new Date(y, m-1, d) 는 넘치는 월·일을 다음 달로 넘긴다
        if (y < 1 || y > 9999) return null;
        try
        {
            return new DateTime(y, 1, 1).AddMonths(mo - 1).AddDays(d - 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>"yyyy-MM-dd". null 은 "".</summary>
    public static string FmtIso(DateTime? d)
        => d is null ? "" : $"{d.Value.Year.ToString(CultureInfo.InvariantCulture)}-{P2(d.Value.Month)}-{P2(d.Value.Day)}";

    /// <summary>"yyMMdd". null 은 "".</summary>
    public static string Fmt6(DateTime? d)
        => d is null ? "" : $"{Yy(d.Value)}{P2(d.Value.Month)}{P2(d.Value.Day)}";

    private static string P2(int n) => n.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');

    // JS: String(year).slice(2)
    private static string Yy(DateTime d)
    {
        var s = d.Year.ToString(CultureInfo.InvariantCulture);
        return s.Length > 2 ? s[2..] : "";
    }
}
