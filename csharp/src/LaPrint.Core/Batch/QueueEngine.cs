// 연속 작업 큐 — 붙여넣기/파일 파싱, SN 연번 전개, 전체 검증, 실행(일시정지/중지) (batch.js).
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using LaPrint.Core.Render;

namespace LaPrint.Core.Batch;

/// <summary>큐 검증 요약의 대표 이슈 한 건 (수준·코드별 개수).</summary>
public sealed record QueueIssueCount(string Level, string Code, string Msg, int Count);

/// <summary>큐 전체 검증 요약 (batch.js validateAll 반환값).</summary>
public sealed record QueueSummary(int Total, int ErrorRows, int WarnRows, int OkRows, IReadOnlyList<QueueIssueCount> Issues);

/// <summary>큐 상태와 실행.</summary>
public sealed class QueueEngine
{
    private readonly List<QueueRow> _rows = new();

    public IReadOnlyList<QueueRow> Rows => _rows;
    public int Count => _rows.Count;

    /// <summary>모든 행의 매수 합.</summary>
    public int TotalLabels
    {
        get
        {
            var n = 0;
            foreach (var r in _rows) n += Math.Max(0, r.Copies);
            return n;
        }
    }

    public bool Running { get; private set; }
    public bool Paused { get; private set; }
    public (int Index, int Total) Progress { get; private set; }

    /// <summary>행·상태가 바뀔 때마다.</summary>
    public event Action? Changed;

    public QueueRow Add(QueueRow r)
        => throw new NotImplementedException("QueueEngine.Add — 아직 구현되지 않았습니다");

    public void AddMany(IEnumerable<QueueRow> rows)
        => throw new NotImplementedException("QueueEngine.AddMany — 아직 구현되지 않았습니다");

    public void Update(string id, Action<QueueRow> edit)
        => throw new NotImplementedException("QueueEngine.Update — 아직 구현되지 않았습니다");

    public void Remove(string id)
        => throw new NotImplementedException("QueueEngine.Remove — 아직 구현되지 않았습니다");

    public void Clear()
        => throw new NotImplementedException("QueueEngine.Clear — 아직 구현되지 않았습니다");

    /// <summary>done/error 를 pending 으로 되돌린다.</summary>
    public void ResetStatus()
        => throw new NotImplementedException("QueueEngine.ResetStatus — 아직 구현되지 않았습니다");

    /// <summary>SN 연번 전개 (from~to, pad 자리 0 채움).</summary>
    public IEnumerable<QueueRow> ExpandSerial(QueueRow proto, int from, int to, int pad)
        => throw new NotImplementedException("QueueEngine.ExpandSerial — 아직 구현되지 않았습니다");

    /// <summary>탭/쉼표 표 텍스트 파싱 — COLUMN_ALIASES 머리글 인식, normDate.</summary>
    public static (List<QueueRow> Rows, Dictionary<string, int> Mapping, bool HeaderDetected, string? Error) ParseTable(string text, QueueRow? defaults)
        => throw new NotImplementedException("QueueEngine.ParseTable — 아직 구현되지 않았습니다");

    /// <summary>csv/txt/tsv 는 ParseTable, xlsx/xls 는 NPOI 첫 시트.</summary>
    public static Task<(List<QueueRow> Rows, Dictionary<string, int> Mapping, bool HeaderDetected, string? Error)> ParseFileAsync(string path, QueueRow? defaults)
        => throw new NotImplementedException("QueueEngine.ParseFileAsync — 아직 구현되지 않았습니다");

    public string ToCsv()
        => throw new NotImplementedException("QueueEngine.ToCsv — 아직 구현되지 않았습니다");

    public QueueSummary ValidateAll(LabelIndex index, FieldMap map, LabelTemplate t, ValidationRules rules, double dpi, Func<QueueRow, RenderContext> ctxOf)
        => throw new NotImplementedException("QueueEngine.ValidateAll — 아직 구현되지 않았습니다");

    /// <summary>큐 실행. prepareJob 은 행마다 await 되며 치환기와 이미지 슬롯을 그 행 데이터로 바꿔야 한다.</summary>
    public Task<BatchResult> RunAsync(LabelTemplate t, ExportOptions o, PdfExporter exporter, Func<QueueRow, Task> prepareJob,
                                      IProgress<(int Index, int Total, QueueRow? Row)>? progress, CancellationToken ct)
        => throw new NotImplementedException("QueueEngine.RunAsync — 아직 구현되지 않았습니다");

    public void Pause()
        => throw new NotImplementedException("QueueEngine.Pause — 아직 구현되지 않았습니다");

    public void Resume()
        => throw new NotImplementedException("QueueEngine.Resume — 아직 구현되지 않았습니다");

    public void Cancel()
        => throw new NotImplementedException("QueueEngine.Cancel — 아직 구현되지 않았습니다");

    private void RaiseChanged() => Changed?.Invoke();

    private void SetRunning(bool running, bool paused, (int Index, int Total) progress)
    {
        Running = running;
        Paused = paused;
        Progress = progress;
        RaiseChanged();
    }
}
