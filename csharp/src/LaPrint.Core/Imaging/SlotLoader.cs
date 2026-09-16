// 이미지 슬롯 읽기 — ★ 행마다 호출. SourceField 가 "@" 로 시작하면 row[열], 아니면 f[SourceField].
using LaPrint.Core.Data;
using LaPrint.Core.Model;

namespace LaPrint.Core.Imaging;

/// <summary>이미지 객체들의 비트맵을 현재 행 데이터로 채운다.</summary>
public static class SlotLoader
{
    /// <summary>하나라도 바뀌었으면 true. 파일명이 아니면 Error = "파일명이 아닙니다: …".</summary>
    public static Task<bool> LoadIntoAsync(IEnumerable<LabelObject> objs, Fields f, DbRow? row, ImageStore? store, bool force, bool autoTransparent, int tol)
        => throw new NotImplementedException("SlotLoader.LoadIntoAsync — 아직 구현되지 않았습니다");
}
