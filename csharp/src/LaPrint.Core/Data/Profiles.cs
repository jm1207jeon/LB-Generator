// 라벨DB 프로필 — 출고처(일반 / BSC 일본)에 따라 다른 DB 파일과 열 매핑을 쓴다 (data.js PROFILES).
namespace LaPrint.Core.Data;

/// <summary>DB 프로필 식별자.</summary>
public enum DbProfileId { General, Bsc }

/// <summary>DB 프로필 정의. Map 은 기본 매핑에서 달라지는 열만 담는다.</summary>
public sealed record DbProfile(DbProfileId Id, string Key, string Name, string DefaultFile,
                               IReadOnlyDictionary<string, string> Map, string Desc);

/// <summary>프로필 목록. 모르는 키는 일반 프로필로 본다.</summary>
public static class Profiles
{
    public static IReadOnlyList<DbProfile> All { get; } = new[]
    {
        new DbProfile(DbProfileId.General, "general", "일반", "01.라벨출력DB",
            new Dictionary<string, string>(), "국내·수출 공통 라벨DB"),
        new DbProfile(DbProfileId.Bsc, "bsc", "BSC 출고 (일본)", "02.라벨출력DB_BSC",
            FieldMap.BscCols, "BSC 전용 라벨DB — AL열부터 열 구성이 다르고 UPN·Catalog 열이 있습니다"),
    };

    /// <summary>키로 프로필을 찾는다. 없으면 첫 번째(일반).</summary>
    public static DbProfile Get(string? key)
    {
        foreach (var p in All)
            if (p.Key == key) return p;
        return All[0];
    }
}
