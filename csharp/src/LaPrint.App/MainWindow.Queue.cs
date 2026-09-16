// 연속 작업 큐 드로어 — 붙여넣기 · 파일 · SN 연번 · 표 · 진행률 · 일시정지/중지
// (app.js: renderQueue/updateQueueStatusCells/renderQueueFull, openDrawer, pasteToQueue, addCurrentToQueue, expandSnDialog,
//  revalidateQueue, noticeRestoredQueue, loadRowToInputs, bind 큐부).
// ★ TODO wave — 이 파일의 본문을 채우는 에이전트가 있다. 시그니처는 바꾸지 않는다.
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LaPrint.Core.Batch;

namespace LaPrint.App;

/// <summary>queueGrid 의 한 행 (QueueRow 를 표시용으로 편 것). Level = pending|running|done|error|warn|skipped.</summary>
public sealed class QueueRowView
{
    public int No { get; set; }
    public string Id { get; set; } = "";
    public string Item { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Sn { get; set; } = "";
    public string Mfg { get; set; } = "";
    public int Months { get; set; }
    public string Exp { get; set; } = "";
    public int Copies { get; set; }
    /// <summary>대기 · 출력 중 · 완료 · 오류 · 건너뜀.</summary>
    public string StatusText { get; set; } = "";
    /// <summary>오류 문구 또는 파일명.</summary>
    public string Message { get; set; } = "";
    public string Level { get; set; } = "pending";
    public QueueRow? Row { get; set; }
}

public partial class MainWindow
{
    /// <summary>queueGrid 에 바인딩되는 표시용 행 목록.</summary>
    internal readonly ObservableCollection<QueueRowView> QueueRows = new();

    /// <summary>선택된 큐 행 id (체크/다중 선택).</summary>
    internal readonly HashSet<string> SelectedQueueRows = new();

    /// <summary>queueGrid.ItemsSource = QueueRows 등 초기화.</summary>
    private void InitQueue() { /* TODO wave */ }

    /// <summary>큐 전체를 다시 그린다 — QueueRows 재구성, queueCount/cntDone/cntErr, queueEmpty/queueGrid, 진행률·일시정지/중지 버튼, UpdatePrintButton (app.js renderQueue/renderQueueFull).</summary>
    private void RenderQueue() { /* TODO wave */ }

    /// <summary>출력 중 상태 열만 갱신 (app.js updateQueueStatusCells).</summary>
    private void UpdateQueueStatusCells() { /* TODO wave */ }

    /// <summary>드로어 펼치기/접기 — drawerBody 표시, drawerCaret 회전, drawerHint (app.js openDrawer).</summary>
    private void OpenDrawer(bool open) { /* TODO wave */ }

    /// <summary>표 텍스트(탭/쉼표)를 큐에 — text 가 null 이면 입력 대화상자 → QueueEngine.ParseTable → 미리보기 확인 → AddMany → RevalidateQueue → OpenDrawer(true) (app.js pasteToQueue).</summary>
    private Task PasteToQueueAsync(string? text = null) => Task.CompletedTask; // TODO wave

    /// <summary>Ctrl+Enter / btnQueueAdd — 품목번호 없으면 안내, 여러 줄 LOT 은 행 여러 개, lot/sn 비움, OpenDrawer(true), 상태줄("큐에 N행 추가 — 총 M행 / K장") (app.js addCurrentToQueue).</summary>
    private void AddCurrentToQueue() { /* TODO wave */ }

    /// <summary>파일 대화상자(csv/tsv/txt/xlsx/xls) → QueueEngine.ParseFileAsync → AddMany (app.js fileQueue.onchange).</summary>
    private Task QueueFromFileAsync() => Task.CompletedTask; // TODO wave

    /// <summary>선택 행 1개를 SN 범위로 펼친다 (app.js expandSnDialog).</summary>
    private Task ExpandSerialAsync() => Task.CompletedTask; // TODO wave

    /// <summary>Queue.ValidateAll → 행별 Issues/Status → RenderQueue (app.js revalidateQueue).</summary>
    private void RevalidateQueue() { /* TODO wave */ }

    /// <summary>RestoredQueue 가 있으면 토스트/상태줄로 알린다 (app.js noticeRestoredQueue).</summary>
    private void NoticeRestoredQueue() { /* TODO wave */ }

    /// <summary>큐 행을 작업 입력으로 불러온다 — ActiveQueueId, jobOrigin("큐 #n") (app.js loadRowToInputs).</summary>
    private void LoadRowToInputs(QueueRow r) { /* TODO wave */ }

    /// <summary>Queue.Changed — RenderQueue, 출력 중이 아니면 ScheduleSave (UI 스레드로 마샬링).</summary>
    private void OnQueueChanged() { /* TODO wave */ }

    /* ---- 핸들러 ---- */

    private void OnDrawerToggleClick(object sender, RoutedEventArgs e) { /* TODO wave: OpenDrawer(drawerBody.Visibility != Visible) */ }
    private void OnQueueAddClick(object sender, RoutedEventArgs e) { /* TODO wave: AddCurrentToQueue() */ }
    private void OnQueuePasteClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(() => PasteToQueueAsync()) */ }
    private void OnQueueFileClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(QueueFromFileAsync) */ }
    /// <summary>빈 행 — 현재 item/mfg/months 로 Queue.Add.</summary>
    private void OnQueueAddRowClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnQueueSerialClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(ExpandSerialAsync) */ }
    private void OnQueueDupClick(object sender, RoutedEventArgs e) { /* TODO wave: 선택 없으면 "복제할 행을 선택하세요." */ }
    private void OnQueueDelClick(object sender, RoutedEventArgs e) { /* TODO wave: 선택 없으면 "삭제할 행을 선택하세요." */ }
    private void OnQueueClearDoneClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>전체 비우기 — 확인("큐의 N행을 모두 지울까요?", danger, "비우기") 후 Queue.Clear.</summary>
    private void OnQueueClearClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>selQueueMode → Settings.Output.Mode 저장.</summary>
    private void OnQueueModeChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    /// <summary>CSV 저장 — 비었으면 "큐가 비어 있습니다.", SaveFileDialog "작업큐_{yyyy-MM-dd}.csv".</summary>
    private void OnQueueExportClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnQueueRunClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(DoPrintQueueAsync) */ }
    private void OnQueuePauseClick(object sender, RoutedEventArgs e) { /* TODO wave: Paused ? Resume : Pause; RenderQueue */ }
    private void OnQueueCancelClick(object sender, RoutedEventArgs e) { /* TODO wave: Queue.Cancel(); SetStatus("출력 중지를 요청했습니다…", Warn) */ }
    /// <summary>행 더블클릭 → LoadRowToInputs.</summary>
    private void OnQueueRowDoubleClick(object sender, MouseButtonEventArgs e) { /* TODO wave */ }
    private void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave: SelectedQueueRows 동기화 */ }
}
