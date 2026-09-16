// 라벨DB 캐시 — 파일 수정 시각이 같으면 12,000행 재파싱을 건너뛴다.
using LaPrint.Core.Data;

namespace LaPrint.Core.Storage;

/// <summary>프로필별 라벨DB 파싱 결과 캐시 (cache 폴더).</summary>
public sealed class DbCache
{
    public void Put(string profile, LabelDb db, DateTime lastWrite)
        => throw new NotImplementedException("DbCache.Put — 아직 구현되지 않았습니다");

    /// <summary>같은 lastWrite 로 저장된 것이 있으면 돌려준다. 없으면 null.</summary>
    public LabelDb? Get(string profile, DateTime lastWrite)
        => throw new NotImplementedException("DbCache.Get — 아직 구현되지 않았습니다");
}
