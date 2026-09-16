// 연속 작업 큐의 한 행 — 입력값·매수·상태·검증 결과 (batch.js row).
using System.Text.Json.Serialization;
using LaPrint.Core.Data;

namespace LaPrint.Core.Batch;

/// <summary>큐 한 행. Status 는 pending | running | done | error | skipped.</summary>
public sealed class QueueRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Item { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Sn { get; set; } = "";
    /// <summary>제조일 (ISO).</summary>
    public string Mfg { get; set; } = "";
    /// <summary>유효일 (ISO). ExpAuto 면 계산값.</summary>
    public string Exp { get; set; } = "";
    public int Months { get; set; } = 36;
    public bool ExpAuto { get; set; } = true;
    public int Copies { get; set; } = 1;
    public string Status { get; set; } = "pending";
    public string FileName { get; set; } = "";
    public string Error { get; set; } = "";
    public List<Issue> Issues { get; set; } = new();

    /// <summary>ValidateAll 이 계산한 필드 (런타임 전용 — 세션에 저장하지 않는다).</summary>
    [JsonIgnore]
    public Fields? Fields { get; set; }

    /// <summary>ValidateAll 이 찾은 라벨DB 행 (런타임 전용 — 세션에 저장하지 않는다).</summary>
    [JsonIgnore]
    public DbRow? Row { get; set; }

    /// <summary>상태 문구 (batch.js STATUS).</summary>
    public static IReadOnlyDictionary<string, string> StatusLabels { get; } = new Dictionary<string, string>
    {
        ["pending"] = "대기",
        ["running"] = "처리중",
        ["done"] = "완료",
        ["error"] = "오류",
        ["skipped"] = "건너뜀",
    };

    /// <summary>입력값·매수만 복사한 새 행 (새 Id, 상태 pending, 검증 결과 없음).</summary>
    public QueueRow CloneInputs() => new()
    {
        Item = Item,
        Lot = Lot,
        Sn = Sn,
        Mfg = Mfg,
        Exp = Exp,
        Months = Months,
        ExpAuto = ExpAuto,
        Copies = Copies,
    };

    /// <summary>작업 입력으로 변환.</summary>
    public JobInputs ToInputs() => new(Item, Lot, Sn, Mfg, Months, ExpAuto, Exp);
}
