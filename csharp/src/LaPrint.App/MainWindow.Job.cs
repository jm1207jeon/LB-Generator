// 작업 입력 · 라벨DB · DB 참조값 · 출력 전 점검 · 칩 (app.js: bind 입력부, setDbProfile, loadDbFromFile, applyDb, autoLoadDb,
// showItemPop/pickItem, renderRefTable, renderChecks, updatePrintButton, updateChips, loadSampleData, noticeDbQuality).
// ★ TODO wave — 이 파일의 본문을 채우는 에이전트가 있다. 시그니처는 바꾸지 않는다.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LaPrint.Core.Data;

namespace LaPrint.App;

/// <summary>DB 참조값 표의 한 줄 (tblRef ItemTemplate: Label · Value · Empty).</summary>
public sealed record RefRow(string Label, string Value, bool Empty);

/// <summary>출력 전 점검의 한 줄 (checks ItemTemplate: Icon ✓/⚠/✕ · Msg · Level error|warn|ok|info).</summary>
public sealed record CheckRow(string Icon, string Msg, string Level, string? ObjId = null);

public partial class MainWindow
{
    /// <summary>selProfile 채우기(Profiles.All) 등 초기화.</summary>
    private void InitJob() { /* TODO wave */ }

    /// <summary>Inputs/Copies → 입력 컨트롤 (app.js syncInputsToUi). 끝에 SyncLabelSizeUi().</summary>
    private void SyncInputsToUi() { /* TODO wave */ }

    /// <summary>selProfile 선택 · BSC 배지(bscBadge) · 콤보 주황 테두리 (app.js syncProfileUi, DESIGN §4-2).</summary>
    private void SyncProfileUi() { /* TODO wave */ }

    /// <summary>입력 컨트롤 → JobInputs (Copies 는 필드에).</summary>
    private JobInputs ReadInputs() => throw new NotImplementedException("MainWindow.ReadInputs — TODO wave");

    /// <summary>품목번호 검색 팝업(itemPop/itemPopList) — Index.Search(inpItem.Text).</summary>
    private void ShowItemPop() { /* TODO wave */ }
    private void HideItemPop() { /* TODO wave */ }

    /// <summary>검색 결과에서 품목을 고른다 → Inputs.Item, HideItemPop, Refresh, inpLot 포커스.</summary>
    private void PickItem(string key) { /* TODO wave */ }

    /// <summary>tblRef (RefRow 목록) + udiFull.</summary>
    private void RenderRefTable() { /* TODO wave */ }

    /// <summary>Preflight.Run → LastPreflight → checks(CheckRow) · checkSummary. 잠금 해제 변경(LayoutDirty)이면 '검증되지 않은 서식' 경고.</summary>
    private void RenderChecks() { /* TODO wave */ }

    /// <summary>btnPrint 활성/힌트("오류를 해결해야 1장 출력을 할 수 있습니다") · btnPrintQueue 표시/문구("큐 N장 출력") · printHint.</summary>
    private void UpdatePrintButton() { /* TODO wave */ }

    /// <summary>chipDb/chipImg/chipOut 점 색·문구·툴팁 (app.js updateChips).</summary>
    private void UpdateChips() { /* TODO wave */ }

    /// <summary>출고 구분 변경 — ProfileKey, Settings.Data.Profile, ApplyProfileToMap, DB 재로딩, RevalidateQueue (app.js setDbProfile).</summary>
    private Task SetDbProfileAsync(string key, bool silent = false) => Task.CompletedTask; // TODO wave

    /// <summary>파일에서 라벨DB 읽기 (DbCache 우선) → ApplyDb. 성공이면 true (app.js loadDbFromFile).</summary>
    private Task<bool> LoadDbFromFileAsync(string path, bool silent = false)
        => throw new NotImplementedException("MainWindow.LoadDbFromFileAsync — TODO wave");

    /// <summary>Db/Index/DbFileName/DbIssues 갱신 → NoticeDbQuality → UpdateChips → RevalidateQueue → Refresh (app.js applyDb).</summary>
    private void ApplyDb(LabelDb db, string fileName) { /* TODO wave */ }

    /// <summary>설정의 dbDir/dbFileName(프로필별)로 시작 시 자동 로딩 (app.js autoLoadDb).</summary>
    private Task AutoLoadDbAsync() => Task.CompletedTask; // TODO wave

    /// <summary>동봉 샘플 DB(샘플_라벨DB.xlsx)·이미지로 시작 (app.js loadSampleData).</summary>
    private Task LoadSampleDataAsync() => Task.CompletedTask; // TODO wave

    /// <summary>Index.Dups/Skipped 를 상태줄·DbIssues 로 알린다 (app.js noticeDbQuality).</summary>
    private void NoticeDbQuality() { /* TODO wave */ }

    /* ---- 입력 핸들러 ---- */

    /// <summary>selProfile — 큐가 있으면 확인("출고 구분을 … 으로 바꾸면 큐에 쌓인 N행을 다시 검증합니다. 계속할까요?") 후 SetDbProfileAsync.</summary>
    private void OnProfileChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }

    /// <summary>inpItem 입력 — Inputs.Item, ShowItemPop, RefreshSoon.</summary>
    private void OnItemTextChanged(object sender, TextChangedEventArgs e) { /* TODO wave */ }
    private void OnItemGotFocus(object sender, KeyboardFocusChangedEventArgs e) { /* TODO wave: ShowItemPop */ }
    private void OnItemLostFocus(object sender, KeyboardFocusChangedEventArgs e) { /* TODO wave: 120ms 뒤 HideItemPop */ }

    /// <summary>inpItem ↑↓ Enter Esc — 팝업 탐색/선택/닫기.</summary>
    private void OnItemKeyDown(object sender, KeyEventArgs e) { /* TODO wave */ }

    /// <summary>itemPopList 클릭 → PickItem.</summary>
    private void OnItemPopPick(object sender, MouseButtonEventArgs e) { /* TODO wave */ }

    /// <summary>inpLot/inpSn/inpMfg/inpMonths/chkExpAuto/inpExp/inpCopies — Inputs 갱신, inpExp 활성, RefreshSoon, 큐 행 편집 중이면 Queue.Update + RevalidateQueue.</summary>
    private void OnInputChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }

    /// <summary>inpLot Enter → inpMfg 포커스.</summary>
    private void OnLotKeyDown(object sender, KeyEventArgs e) { /* TODO wave */ }

    private void OnMapDataClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(OpenDataMapperAsync) */ }

    /// <summary>btnClearJob — lot/sn 비움, copies 1, ActiveQueueId null, jobOrigin "새 작업", SyncInputsToUi, Refresh, inpLot 포커스.</summary>
    private void OnClearJobClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
}
