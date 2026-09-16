// 작업 1건(필드+행) 검증 — 필수 입력·GTIN·유효기간 순서 등 (data.js validateRecord / lotHint).
using System.Text.RegularExpressions;
using LaPrint.Core.Barcode;

namespace LaPrint.Core.Data;

/// <summary>필드 값 검증. 출력 전 점검의 데이터 부분. error = 출력 차단, warn = 경고 후 진행 가능.</summary>
public static class RecordValidator
{
    private static readonly Regex SpaceRe = new(@"\s", RegexOptions.Compiled);
    private static readonly Regex SpecialRe = new(@"[^A-Za-z0-9_\-]", RegexOptions.Compiled);

    /// <summary>오늘 날짜 기준으로 검증한다.</summary>
    public static IReadOnlyList<Issue> Validate(Fields f, DbRow? row, ValidationRules r)
        => Validate(f, row, r, DateTime.Today);

    /// <summary>기준일(today)을 지정해 검증한다 — EXP_PAST 판정에 쓴다.</summary>
    public static IReadOnlyList<Issue> Validate(Fields f, DbRow? row, ValidationRules r, DateTime today)
    {
        r ??= new ValidationRules();
        var outList = new List<Issue>();
        void Add(string level, string code, string msg, string? field = null) => outList.Add(new Issue(level, code, msg, field));

        var item = f.Get("ITEM");
        var lot = f.Get("LOT");
        var sn = f.Get("SN");
        var mfgS = f.Get("MFG");
        var expS = f.Get("EXP");

        if (r.RequireItem && item.Length == 0) Add("error", "NO_ITEM", "품목번호를 입력하세요.", "item");
        else if (item.Length > 0 && row is null) Add("error", "ITEM_NOT_FOUND", $"품목번호 \"{item}\"를 라벨DB에서 찾을 수 없습니다.", "item");

        if (r.RequireLot && lot.Length == 0) Add("error", "NO_LOT", "LOT 번호를 입력하세요.", "lot");
        if (r.RequireSn && sn.Length == 0) Add("error", "NO_SN", "SN을 입력하세요.", "sn");
        if (r.RequireMfg && mfgS.Length == 0) Add("error", "NO_MFG", "제조일(MFG)을 입력하세요.", "mfg");

        if (expS.Length == 0 && mfgS.Length > 0) Add("warn", "NO_EXP", "유효일(EXP)이 계산되지 않았습니다. 유효기간 개월 수를 확인하세요.", "exp");

        var mfg = f.MfgDate;
        var exp = f.ExpDate;
        if (r.CheckExpOrder && mfg is not null && exp is not null && exp.Value <= mfg.Value)
            Add("error", "EXP_ORDER", $"유효일({expS})이 제조일({mfgS})보다 빠르거나 같습니다.", "exp");
        if (r.CheckExpPast && exp is not null)
        {
            if (exp.Value < today.Date) Add("warn", "EXP_PAST", $"유효일({expS})이 이미 지났습니다.", "exp");
        }
        if (row is not null)
        {
            var gtin = f.Get("GTIN");
            if (gtin.Length == 0) Add("warn", "NO_GTIN", "이 품목에 GTIN이 없습니다. UDI 바코드를 만들 수 없습니다.");
            else if (r.CheckGtin && !Gs1.GtinValid(gtin))
            {
                var fix = Gs1.GtinCheckDigit(gtin[..^1]);
                Add("error", "GTIN_CHECK", $"GTIN 체크디짓이 틀렸습니다: {gtin} (올바른 마지막 자리: {fix})");
            }
            if (f.Get("REF").Length == 0) Add("warn", "NO_REF", "이 품목에 규격(REF)이 없습니다.");
            if (f.Get("PRODUCT").Length == 0) Add("warn", "NO_PRODUCT", "이 품목에 제품명이 없습니다.");
        }
        return outList;
    }

    /// <summary>LOT 형식이 평소와 다를 때의 힌트 문구. 사내 규칙을 강제하지는 않고 안내만. 없으면 null.</summary>
    public static string? LotHint(string lot)
    {
        var s = lot ?? "";
        if (s.Length == 0) return null;
        if (SpaceRe.IsMatch(s)) return "LOT에 공백이 들어 있습니다.";
        if (SpecialRe.IsMatch(s)) return "LOT에 특수문자가 들어 있습니다. 바코드 판독에 문제가 될 수 있습니다.";
        return null;
    }
}
