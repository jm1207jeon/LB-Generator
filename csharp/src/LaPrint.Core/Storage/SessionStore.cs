// 세션 저장소 — 마지막 작업 입력·큐·잠금 상태를 다음 실행에 복원한다 (app.js session).
using LaPrint.Core.Batch;
using LaPrint.Core.Data;

namespace LaPrint.Core.Storage;

/// <summary>저장되는 세션 상태.</summary>
public sealed class SessionState
{
    public JobInputs? Inputs { get; set; }
    public List<QueueRow> Queue { get; set; } = new();
    public bool Locked { get; set; } = true;
    public DateTime SavedAt { get; set; }
}

/// <summary>session.json 읽기·쓰기.</summary>
public sealed class SessionStore
{
    public SessionState? Load()
        => throw new NotImplementedException("SessionStore.Load — 아직 구현되지 않았습니다");

    public void Save(SessionState s)
        => throw new NotImplementedException("SessionStore.Save — 아직 구현되지 않았습니다");
}
