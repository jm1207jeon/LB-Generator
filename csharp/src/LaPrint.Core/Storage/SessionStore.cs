// 세션 저장소 — 마지막 작업 입력·큐·잠금 상태를 다음 실행에 복원한다 (app.js scheduleSave/restore 의 session).
using System.Text.Json;
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
    /// <summary>없거나 깨졌으면 null. 처리중이던 행은 대기로 되돌린다.</summary>
    public SessionState? Load()
    {
        var path = AppPaths.SessionFile;
        if (!File.Exists(path)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), StorageJson.Indented);
            if (s is null) return null;
            s.Queue ??= new List<QueueRow>();
            s.Queue.RemoveAll(r => r is null);
            foreach (var r in s.Queue)
            {
                if (r.Status == "running") r.Status = "pending";
                r.Issues ??= new List<Issue>();
            }
            return s;
        }
        catch (Exception ex)
        {
            AppLog.Warn("세션 파일을 읽을 수 없습니다: " + path, ex);
            return null;
        }
    }

    public void Save(SessionState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.SavedAt == default) s.SavedAt = DateTime.Now;
        StorageJson.WriteAtomic(AppPaths.SessionFile, JsonSerializer.Serialize(s, StorageJson.Indented));
    }
}
