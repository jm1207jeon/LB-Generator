// 라벨DB 한 행과 읽어 온 DB 전체 — 열 문자("A".."BC") → 표시 문자열.
namespace LaPrint.Core.Data;

/// <summary>라벨DB 한 행. 열 문자 → 셀 표시 문자열(trim).</summary>
public sealed class DbRow : Dictionary<string, string> { }

/// <summary>읽어 온 라벨DB. Sheets 는 (시트 이름, 키 열 데이터 수).</summary>
public sealed record LabelDb(List<DbRow> Rows, string Sheet, IReadOnlyList<(string Name, int Count)> Sheets,
                             DbRow? Header, int ColCount);

/// <summary>NPOI 로 .xls/.xlsx/.xlsm/.csv 를 읽는다. 셀 값은 표시 문자열 그대로(앞자리 0 보존).</summary>
public static class LabelDbLoader
{
    public static LabelDb Load(Stream s, string fileName, string keyCol)
        => throw new NotImplementedException("LabelDbLoader.Load — 아직 구현되지 않았습니다");
}
