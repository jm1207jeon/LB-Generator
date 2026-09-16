// 샘플 라벨DB(12행) + 동봉 이미지 폴더 — 슬롯 로딩·렌더 회귀 테스트가 함께 쓴다.
using LaPrint.Core.Data;
using LaPrint.Core.Imaging;

namespace LaPrint.Core.Tests.Imaging;

/// <summary>Fixtures/샘플_라벨DB.xlsx 를 한 번만 읽어 색인과 이미지 저장소를 만든다.</summary>
public static class SampleDb
{
    private static readonly Lazy<(LabelDb Db, FieldMap Map, LabelIndex Index)> Loaded = new(() =>
    {
        using var fs = File.OpenRead(Fixtures.Path("샘플_라벨DB.xlsx"));
        var db = LabelDbLoader.Load(fs, "샘플_라벨DB.xlsx", "H");
        var map = new FieldMap("general");
        var idx = LabelIndex.Build(db.Rows, map);
        return (db, map, idx);
    });

    public static LabelDb Db => Loaded.Value.Db;
    public static FieldMap Map => Loaded.Value.Map;
    public static LabelIndex Index => Loaded.Value.Index;

    /// <summary>동봉 이미지 폴더 (Fixtures/images).</summary>
    public static ImageStore NewStore() => new(Fixtures.Path("images"));

    /// <summary>품목번호 행 (없으면 실패).</summary>
    public static DbRow Row(string item)
        => Index.ByRef.TryGetValue(item, out var r) ? r : throw new InvalidOperationException($"샘플 DB 에 {item} 이 없습니다");

    /// <summary>품목번호 + LOT L1 + 제조일 2026-06-01 로 계산한 필드.</summary>
    public static Fields FieldsOf(string item, string lot = "L1", string mfg = "2026-06-01")
        => FieldComputer.Compute(Row(item), new JobInputs(item, lot, "", mfg), Map);
}
