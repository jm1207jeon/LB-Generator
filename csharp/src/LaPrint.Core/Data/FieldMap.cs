// 라벨DB 열 매핑 — 프로필 기본값 위에 사용자 수정을 덮어쓴다 (data.js DEFAULT_FIELD_COLS 계열).
namespace LaPrint.Core.Data;

/// <summary>필드 키 → 라벨DB 열 문자 매핑. 빈 값은 '사용 안 함'.</summary>
public sealed class FieldMap
{
    /// <summary>엑셀 수식이 참조하던 열을 그대로 옮긴 기본 매핑. BSC 전용 항목은 "".</summary>
    public static IReadOnlyDictionary<string, string> DefaultCols { get; } = new Dictionary<string, string>
    {
        ["REF"] = "L", ["GTIN"] = "AJ", ["PRODUCT"] = "AU", ["PRODUCT_EN"] = "I", ["MDR"] = "BB", ["REV"] = "AK",
        ["REF_MTW"] = "M", ["REF_CN"] = "N",
        ["STENT_OD"] = "Q", ["STENT_LEN"] = "R", ["HEAD_OD"] = "S", ["HEAD_LEN_D"] = "T", ["HEAD_LEN_P"] = "U", ["DIM_B"] = "B",
        ["GW_INCH"] = "V", ["GW_MM"] = "W", ["DD_FR"] = "X", ["DD_MM"] = "Y", ["DD_LEN"] = "Z",
        ["LIFETIME"] = "AY", ["MDD_LIFE"] = "AX", ["PIC_NOTE"] = "AZ", ["COVER"] = "BC", ["MDD_NOTE"] = "AV",
        ["TERM"] = "AW", ["DEVICE"] = "BA",
        ["KOREA_NO"] = "AA", ["KOREA_NAME"] = "AB", ["JAPAN_NO"] = "AC", ["JAPAN_NAME"] = "AD",
        ["CHINA_NO"] = "AE", ["CHINA_STD"] = "AG", ["CHINA_NAME"] = "AH", ["UKR"] = "AQ", ["DOMESTIC"] = "AR",
        ["IMG_NAME1"] = "J", ["IMG_NAME2"] = "K", ["IMG_STENT"] = "O", ["IMG_DELIVERY"] = "P",
        ["IMG_AM"] = "AM", ["IMG_AP"] = "AP",
        ["UPN"] = "", ["CATALOG"] = "", ["STENT_TYPE"] = "", ["REF_JP"] = "",
    };

    /// <summary>BSC DB 에서 달라지는 열만. 값을 확인하지 못한 열은 일부러 비워 둔다.</summary>
    public static IReadOnlyDictionary<string, string> BscCols { get; } = new Dictionary<string, string>
    {
        ["PRODUCT"] = "J",
        ["IMG_NAME1"] = "",
        ["IMG_NAME2"] = "K",
        ["REF_JP"] = "AM",
        ["UPN"] = "AQ",
        ["CATALOG"] = "AR",
        ["STENT_TYPE"] = "AS",
        ["IMG_AM"] = "", ["IMG_AP"] = "", ["UKR"] = "", ["DOMESTIC"] = "",
        ["MDD_NOTE"] = "", ["TERM"] = "", ["MDD_LIFE"] = "", ["LIFETIME"] = "",
        ["PIC_NOTE"] = "", ["DEVICE"] = "", ["MDR"] = "", ["COVER"] = "",
    };

    /// <summary>필드 키의 한국어 이름.</summary>
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>
    {
        ["ITEM"] = "품목번호", ["LOT"] = "LOT", ["SN"] = "SN", ["MFG"] = "제조일", ["EXP"] = "유효일", ["EXP6"] = "유효일(YYMMDD)",
        ["MFG6"] = "제조일(YYMMDD)", ["REF"] = "규격(REF)", ["GTIN"] = "GTIN", ["PRODUCT"] = "제품명", ["PRODUCT_EN"] = "제품명(영문)",
        ["MDR"] = "MDR 추가문구", ["REV"] = "개정번호", ["STENT_OD"] = "스텐트 외경", ["STENT_LEN"] = "스텐트 길이",
        ["HEAD_OD"] = "헤드 외경", ["HEAD_LEN_D"] = "헤드 길이(원위)", ["HEAD_LEN_P"] = "헤드 길이(근위)",
        ["GW_INCH"] = "가이드와이어(inch)", ["GW_MM"] = "가이드와이어(mm)", ["DD_FR"] = "딜리버리 외경(Fr)",
        ["DD_MM"] = "딜리버리 외경(mm)", ["DD_LEN"] = "딜리버리 유효길이", ["LIFETIME"] = "MDR 유지일",
        ["MDD_LIFE"] = "MDD 유지일", ["PIC_NOTE"] = "환자카드 추가문구", ["COVER"] = "Cover type",
        ["KOREA_NO"] = "국내 허가번호", ["KOREA_NAME"] = "국내 제품명", ["JAPAN_NO"] = "일본 허가번호",
        ["JAPAN_NAME"] = "일본 제품명", ["CHINA_NO"] = "중국 허가번호", ["CHINA_NAME"] = "중국 제품명",
        ["UDI_FULL"] = "UDI 전체", ["UDI_L1"] = "UDI 1행", ["UDI_L2"] = "UDI 2행", ["GTIN01"] = "UDI-DI (01)",
        ["IMG_NAME1"] = "제품명 그림(1줄)", ["IMG_NAME2"] = "제품명 그림(2줄)", ["IMG_STENT"] = "스텐트 그림",
        ["IMG_DELIVERY"] = "딜리버리 그림", ["IMG_AM"] = "STENT OD 그림", ["IMG_AP"] = "CI 그림",
        ["REF_MTW"] = "규격(독일 MTW)", ["REF_CN"] = "규격(중국)", ["DIM_B"] = "스텐트 몸통 길이",
        ["CHINA_STD"] = "중국 제품표준", ["UKR"] = "우크라이나 형명", ["DOMESTIC"] = "국내 형명",
        ["MDD_NOTE"] = "MDD 추가문구", ["TERM"] = "기한", ["DEVICE"] = "삽입기구",
        ["UPN"] = "BSC UPN", ["CATALOG"] = "BSC Catalog/Ref #", ["STENT_TYPE"] = "BSC 스텐트구분",
        ["REF_JP"] = "형명 (일본/말레이시아)",
        ["TODAY"] = "오늘 날짜", ["NOW"] = "현재 시각",
    };

    /// <summary>데이터 매칭 편집기에서 묶어 보여줄 분류.</summary>
    public static IReadOnlyList<(string Group, string[] Keys)> Groups { get; } = new (string, string[])[]
    {
        ("식별", new[] { "REF", "GTIN", "PRODUCT", "PRODUCT_EN", "MDR", "REV", "REF_MTW", "REF_CN" }),
        ("스텐트 치수", new[] { "STENT_OD", "STENT_LEN", "HEAD_OD", "HEAD_LEN_D", "HEAD_LEN_P", "DIM_B" }),
        ("딜리버리 치수", new[] { "GW_INCH", "GW_MM", "DD_FR", "DD_MM", "DD_LEN" }),
        ("규제 · 문구", new[] { "LIFETIME", "MDD_LIFE", "PIC_NOTE", "COVER", "MDD_NOTE", "TERM", "DEVICE" }),
        ("국가별 인허가", new[] { "KOREA_NO", "KOREA_NAME", "JAPAN_NO", "JAPAN_NAME", "CHINA_NO", "CHINA_STD", "CHINA_NAME", "UKR", "DOMESTIC" }),
        ("이미지 파일명", new[] { "IMG_NAME1", "IMG_NAME2", "IMG_STENT", "IMG_DELIVERY", "IMG_AM", "IMG_AP" }),
        ("BSC 전용", new[] { "REF_JP", "UPN", "CATALOG", "STENT_TYPE" }),
    };

    /// <summary>값이 그림 파일명인 필드.</summary>
    public static string[] ImageFields { get; } = { "IMG_NAME1", "IMG_NAME2", "IMG_STENT", "IMG_DELIVERY", "IMG_AM", "IMG_AP" };

    private readonly Dictionary<string, string> _cols;

    public FieldMap() : this("general") { }

    public FieldMap(string profile)
    {
        Profile = Profiles.Get(profile).Key;
        _cols = new Dictionary<string, string>(BaseCols());
    }

    /// <summary>현재 프로필 키 ("general" | "bsc").</summary>
    public string Profile { get; private set; }

    /// <summary>품목번호가 있는 열.</summary>
    public string KeyCol { get; set; } = "H";

    /// <summary>현재 유효 매핑 (프로필 기본값 + 사용자 수정).</summary>
    public IReadOnlyDictionary<string, string> Cols => _cols;

    /// <summary>프로필을 바꾸고 매핑을 그 프로필 기본값으로 되돌린다. 모르는 키는 일반.</summary>
    public void SetProfile(string key)
    {
        Profile = Profiles.Get(key).Key;
        Reset();
    }

    /// <summary>프로필 기본값에서 출발해 사용자 매핑을 덮어쓴다. 값은 trim + 대문자, "" 은 사용 안 함.</summary>
    public void Apply(IDictionary<string, string>? userMap)
    {
        _cols.Clear();
        foreach (var kv in BaseCols()) _cols[kv.Key] = kv.Value;
        if (userMap is null) return;
        foreach (var kv in userMap)
        {
            if (!DefaultCols.ContainsKey(kv.Key)) continue;
            _cols[kv.Key] = (kv.Value ?? "").Trim().ToUpperInvariant();
        }
    }

    /// <summary>사용자 수정을 모두 버리고 프로필 기본값으로.</summary>
    public void Reset() => Apply(null);

    /// <summary>프로필 기본값과 다른 항목만 (설정 저장용).</summary>
    public Dictionary<string, string> Diff()
    {
        var baseCols = BaseCols();
        var diff = new Dictionary<string, string>();
        foreach (var kv in baseCols)
        {
            var cur = _cols.TryGetValue(kv.Key, out var v) ? v : "";
            if (cur != kv.Value) diff[kv.Key] = cur;
        }
        return diff;
    }

    /// <summary>현재 프로필의 기본 매핑 (사용자 수정 전) = DefaultCols + 프로필 Map.</summary>
    public IReadOnlyDictionary<string, string> BaseCols()
    {
        var d = new Dictionary<string, string>(DefaultCols);
        foreach (var kv in Profiles.Get(Profile).Map) d[kv.Key] = kv.Value;
        return d;
    }
}
