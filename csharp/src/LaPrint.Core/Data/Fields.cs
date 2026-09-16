// 작업 입력과 계산된 필드 — 엑셀 원본(PML-001 Rev.1) 수식과 1:1 (data.js computeFields).
namespace LaPrint.Core.Data;

/// <summary>작업 입력. Mfg 는 ISO 날짜 문자열, Months 는 유효기간(개월).</summary>
public sealed record JobInputs(string Item, string Lot, string Sn, string Mfg, int Months = 36,
                               bool ExpAuto = true, string Exp = "");

/// <summary>계산된 필드 키 → 문자열. 날짜는 파싱된 값도 함께 가진다.</summary>
public sealed class Fields : Dictionary<string, string>
{
    public DateTime? MfgDate { get; set; }
    public DateTime? ExpDate { get; set; }
}

/// <summary>필드 계산 — ITEM/LOT/SN + 모든 열 + MFG/MFG6/EXP/EXP6 + UDI + TODAY/NOW/DATE/TIME.</summary>
public static class FieldComputer
{
    public static Fields Compute(DbRow? row, JobInputs inputs, FieldMap map)
        => throw new NotImplementedException("FieldComputer.Compute — 아직 구현되지 않았습니다");

    /// <summary>엑셀 EDATE (말일 클램프).</summary>
    public static DateTime Edate(DateTime d, int months)
        => throw new NotImplementedException("FieldComputer.Edate — 아직 구현되지 않았습니다");

    /// <summary>YYYY YY MMM MM M DD D HH mm ss 패턴.</summary>
    public static string FormatDate(DateTime? d, string pattern)
        => throw new NotImplementedException("FieldComputer.FormatDate — 아직 구현되지 않았습니다");
}
