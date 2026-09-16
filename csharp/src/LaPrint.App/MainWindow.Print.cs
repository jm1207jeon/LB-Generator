// 출력(PDF · ZEBRA) · 용지 배치 · 원판 떼어내기 · 서식(저장/불러오기/관리/프리셋) · 잠금 · 실물 보기
// (app.js: doPrintSingle/doPrintQueue/printSingle/printQueue/printZebra/confirmDialog/confirmZebra, loadSlotImages(Into),
//  layoutOpt/putLayout/syncPaperUi/openPaperDialog/openExtractDialog, installTemplate/applyPresetTemplate/applyDefaultTemplate/
//  refreshTemplateList/saveTemplate/loadTemplate/manageTemplates, setLocked/setPreview).
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaPrint.App.Services;
using LaPrint.App.Windows;
using LaPrint.Core.Batch;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Storage;
using LaPrint.Core.Zpl;
using SkiaSharp;

namespace LaPrint.App;

public partial class MainWindow
{
    private const string SheetTplName = "A3 라벨 세트 원판";
    private const string PresetPrefix = "__preset__:";

    private bool _syncingPaper;
    private bool _fillingTemplates;
    /// <summary>그림 슬롯 읽기가 겹치지 않게 (타이핑 중 Refresh 가 자주 온다).</summary>
    private readonly SemaphoreSlim _slotGate = new(1, 1);
    /// <summary>내장 배경(template_bg.png) 디코딩 결과 — 서식마다 다시 읽지 않는다.</summary>
    private SKBitmap? _templateBg;
    private string? _bgKey;
    private SKBitmap? _bgBitmap;

    /// <summary>selPaper 채우기(Paper.Papers) 등 초기화.</summary>
    private void InitPrint()
    {
        _syncingPaper = true;
        selPaper.Items.Clear();
        foreach (var p in Paper.Papers)
            selPaper.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        _syncingPaper = false;
    }

    /* ================= 출력 문맥 도우미 ================= */

    /// <summary>출력 옵션 — 설정(output + layout) 을 그대로 옮긴다.</summary>
    private ExportOptions MakeExportOptions() => new()
    {
        Mode = Settings.Output.Mode == "merged" ? "merged" : "separate",
        Dpi = Settings.Output.Dpi > 0 ? Settings.Output.Dpi : 300,
        IncludeBg = Settings.Output.IncludeBg && Template.Label.BgInclude,
        Pattern = string.IsNullOrWhiteSpace(Settings.Output.Pattern) ? "{ITEM}_{LOT}_{DATE}" : Settings.Output.Pattern,
        Conflict = Settings.Output.Conflict,
        OutDir = OutDirOrDownloads(),
        Layout = Settings.Layout,
    };

    /// <summary>주어진 필드·행으로 그리는 문맥. 큐 실행처럼 화면 상태와 다른 건을 그릴 때 쓴다.</summary>
    private RenderContext MakeRenderContext(Fields f, DbRow? row, bool forExport)
    {
        return new RenderContext
        {
            Fields = f,
            Row = row,
            ResolveText = t => Placeholders.Resolve(t ?? "", f, row),
            Objects = Template.Objects,
            ImageOf = ImageOf,
            ForExport = forExport,
            IncludeBg = Settings.Output.IncludeBg && Template.Label.BgInclude,
            Background = BgBitmap(),
            Dpi = Settings.Output.Dpi > 0 ? Settings.Output.Dpi : 300,
        };
    }

    /// <summary>현재 서식의 배경 비트맵 (내장 배경 또는 파일). 없으면 null.</summary>
    private SKBitmap? BgBitmap()
    {
        var bg = Template.Label.Bg ?? "";
        if (bg.Length == 0) return null;
        if (bg == LabelSize.TemplateBg)
        {
            try { _templateBg ??= SKBitmap.Decode(TemplateJson.LoadBackgroundPng()); }
            catch (Exception ex) { AppLog.Warn("내장 배경을 읽지 못했습니다: " + ex.Message); }
            return _templateBg;
        }
        if (_bgKey == bg) return _bgBitmap;
        _bgKey = bg;
        _bgBitmap = null;
        try { if (File.Exists(bg)) _bgBitmap = SKBitmap.Decode(bg); }
        catch (Exception ex) { AppLog.Warn("배경 그림을 읽지 못했습니다: " + ex.Message); }
        return _bgBitmap;
    }

    /// <summary>1장 PDF · ZEBRA 전송처럼 QueueEngine 을 거치지 않는 출력이 진행 중인지. 모든 잠금 판단은 IsPrinting 을 쓴다.</summary>
    private bool _printBusy;

    /// <summary>ZEBRA 연속 전송 중지용 (창 닫기).</summary>
    private CancellationTokenSource? _zebraCts;

    /// <summary>어떤 경로로든 출력이 진행 중인가 — 이 동안 두 번째 출력·편집·설정 변경은 막는다 (DESIGN §4-9).</summary>
    internal bool IsPrinting => Queue.Running || _printBusy;

    /// <summary>출력 중 입력·서식 변경 잠금 (DESIGN §4-9). 큐 드로어의 일시정지/중지는 RenderQueue 가 맡는다.</summary>
    private void SetBusy(bool busy)
    {
        _printBusy = busy;
        gboxJob.IsEnabled = !busy;
        btnSettings.IsEnabled = !busy;
        chips.IsEnabled = !busy;
        gboxRef.IsEnabled = !busy;
        stageBar.IsEnabled = !busy;
        inspector.IsEnabled = !busy;
        selTemplate.IsEnabled = !busy;
        btnTplSave.IsEnabled = !busy;
        btnTplManage.IsEnabled = !busy;
        btnLock.IsEnabled = !busy;
        selTarget.IsEnabled = !busy;
        selPaper.IsEnabled = !busy;
        btnPaperSetup.IsEnabled = !busy;
        btnPrinterSetup.IsEnabled = !busy;
        UpdatePrintButton();
    }

    /* ---- 출력 진입 (HFE: 1장과 큐는 언제나 다른 명령) ---- */

    /// <summary>Ctrl+P / btnPrint — 오류 있으면 거부, target 에 따라 PrintSingleAsync 또는 PrintZebraAsync(false).</summary>
    private Task DoPrintSingleAsync()
    {
        if (IsPrinting) return Task.CompletedTask;
        return Settings.Output.Target == "zebra" ? PrintZebraAsync(false) : PrintSingleAsync();
    }

    /// <summary>Ctrl+Shift+P / btnPrintQueue — 큐 비었으면 안내, target 에 따라 PrintQueueAsync 또는 PrintZebraAsync(true).</summary>
    private Task DoPrintQueueAsync()
    {
        if (IsPrinting) return Task.CompletedTask;
        if (Queue.Count == 0) { Toast("큐가 비어 있습니다.", ToastLevel.Warn); return Task.CompletedTask; }
        // 큰 큐의 배경 검증이 끝나기 전에는 상태가 채워지지 않은 행이 있다 — 검증 결과 없이 출력하면 안 된다
        if (_queueValidating) { Toast("큐를 검증하는 중입니다. 검증이 끝난 뒤 출력하세요.", ToastLevel.Warn); return Task.CompletedTask; }
        return Settings.Output.Target == "zebra" ? PrintZebraAsync(true) : PrintQueueAsync();
    }

    /// <summary>1장 PDF: 슬롯 이미지 → Preflight → ConfirmDialogAsync → Exporter.ExportOne → History.Append → 상태줄.</summary>
    private async Task PrintSingleAsync()
    {
        await LoadSlotImagesAsync(false);
        RenderChecks();
        var pf = LastPreflight!;
        if (pf.Errors.Count > 0)
        {
            Toast("오류를 해결해야 출력할 수 있습니다.", ToastLevel.Err);
            return;
        }
        var f = Fields;
        var row = Row;
        var copies = Math.Max(1, Copies);
        if (Settings.Output.ConfirmBeforeExport)
        {
            var jobs = new[] { new ExportJob("single", copies, f, row, Template.Objects) };
            if (!await ConfirmDialogAsync(jobs, pf)) return;
        }
        SetBusy(true);
        try
        {
            SetStatus("PDF 만드는 중…");
            var opt = MakeExportOptions();
            var ctx = MakeRenderContext(f, row, forExport: true);
            var res = await Task.Run(() => Exporter.ExportOne(Template, ctx, opt, CancellationToken.None));
            if (!res.Ok) throw new InvalidOperationException(res.Error ?? "출력 실패");
            History.Append(new HistoryEntry
            {
                Item = f.Get("ITEM"), Ref = f.Get("REF"), Lot = f.Get("LOT"), Sn = f.Get("SN"),
                Mfg = f.Get("MFG"), Exp = f.Get("EXP"), Udi = f.Get("UDI_FULL"),
                // PDF 1장 출력은 파일 하나(1쪽)를 만든다 — 품질기록에는 실제로 만든 수량을 남긴다 (app.js copies: 1)
                Copies = 1, FileName = res.FileName ?? "", Ok = true, Mode = "single", Profile = ProfileKey,
            });
            var where = string.IsNullOrWhiteSpace(Settings.Paths.OutDir) ? " (다운로드)" : " → " + DirName(Settings.Paths.OutDir);
            SetStatus($"저장 완료: {res.FileName}{where}");
            Toast("출력 완료: " + res.FileName, ToastLevel.Ok);
            inpLot.Focus();
            inpLot.SelectAll();
        }
        catch (Exception ex)
        {
            AppLog.Error("출력 실패", ex);
            SetStatus("출력 실패: " + ex.Message, StatusLevel.Error);
            Toast("출력 실패: " + ex.Message, ToastLevel.Err);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>큐 PDF: RevalidateQueue → ConfirmDialogAsync(queueMode) → Queue.RunAsync(prepareJob = 행마다 필드+이미지 슬롯 교체 ★) → 요약.</summary>
    private async Task PrintQueueAsync()
    {
        RevalidateQueue();
        var pending = Queue.Rows.Where(r => r.Status != "done").ToList();
        if (pending.Count == 0) { Toast("출력할 행이 없습니다. 모두 완료 상태입니다.", ToastLevel.Warn); return; }
        var errN = pending.Count(r => r.Status == "error");
        var okRows = pending.Where(r => r.Status != "error").ToList();
        if (okRows.Count == 0) { Toast("모든 행에 오류가 있어 출력할 수 없습니다.", ToastLevel.Err); return; }

        var jobs = okRows.Select(r => new ExportJob(r.Id, Math.Max(1, r.Copies), r.Fields ?? FieldsOf(r, out _), r.Row, Template.Objects)).ToList();
        var total = jobs.Sum(j => j.Copies);
        // 확인창에는 경고를 코드별로 모아 개수와 함께 보인다 (batch.js validateAll summary.issues)
        var warnGroups = okRows.SelectMany(r => r.Issues).Where(i => i.Level == "warn")
            .GroupBy(i => i.Code)
            .Select(g => new Issue("warn", g.Key, g.First().Msg + (g.Count() > 1 ? $" ({g.Count()}건)" : "")))
            .ToList();
        var pf = new PreflightResult(warnGroups, Array.Empty<Issue>(), warnGroups, Array.Empty<Issue>());
        if (!await ConfirmDialogAsync(jobs, pf, queueMode: true, total: total, skipped: errN)) return;

        OpenDrawer(true);
        SetBusy(true);
        var opt = MakeExportOptions();
        try
        {
            var progress = new Progress<(int Index, int Total, QueueRow? Row)>(p =>
            {
                if (p.Row is not null) SetStatus($"{p.Index}/{p.Total} 출력 중: {p.Row.Item} LOT {p.Row.Lot}");
                RenderQueue();
            });
            // ★ 행마다 그 제품의 필드와 그림을 다시 읽는다 (앞 행 사진이 남지 않도록). 스레드 풀에서 불릴 수 있다.
            Func<QueueRow, Task> prepareJob = async r =>
            {
                var f = FieldsOf(r, out var row);
                r.Fields = f;
                r.Row = row;
                await LoadSlotImagesIntoAsync(Template.Objects, f, row, true);
            };
            var res = await Task.Run(() => Queue.RunAsync(Template, opt, Exporter, prepareJob, progress, CancellationToken.None));
            RenderQueue();
            AppendQueueHistory(res, okRows);
            if (res.Cancelled)
            {
                SetStatus($"출력을 중지했습니다. {res.Done}장 완료.", StatusLevel.Warn);
                Toast($"중지됨 — {res.Done}장 출력", ToastLevel.Warn);
            }
            else if (!res.Ok && res.Done == 0 && !string.IsNullOrEmpty(res.Error))
            {
                throw new InvalidOperationException(res.Error);
            }
            else
            {
                SetStatus($"연속 출력 완료: 성공 {res.Done}장{(res.Failed > 0 ? $", 실패 {res.Failed}건" : "")}{(errN > 0 ? $", 오류로 건너뜀 {errN}행" : "")}");
                Toast($"출력 완료 — {res.Done}장{(res.Failed > 0 ? $" (실패 {res.Failed})" : "")}", res.Failed > 0 ? ToastLevel.Warn : ToastLevel.Ok);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("연속 출력 실패", ex);
            SetStatus("연속 출력 실패: " + ex.Message, StatusLevel.Error);
            Toast("연속 출력 실패: " + ex.Message, ToastLevel.Err);
        }
        finally
        {
            SetBusy(false);
            await LoadSlotImagesAsync(true);      // 화면을 현재 작업 건의 그림으로 되돌린다
            Editor.Invalidate();
            RenderQueue();
            UpdatePrintButton();
        }
    }

    /// <summary>큐 행의 필드·DB 행 (색인 조회 + FieldComputer).</summary>
    private Fields FieldsOf(QueueRow r, out DbRow? row)
    {
        row = null;
        if (Index is not null) Index.ByRef.TryGetValue((r.Item ?? "").Trim(), out row);
        return FieldComputer.Compute(row, r.ToInputs(), Map);
    }

    /// <summary>연속 출력 결과를 출력 이력에 남긴다 (품질기록).</summary>
    private void AppendQueueHistory(BatchResult res, List<QueueRow> rows)
    {
        var byId = rows.ToDictionary(r => r.Id);
        var mode = Settings.Output.Mode == "merged" ? "merged" : "separate";
        // 용지 모드는 매수만큼 결과가 펼쳐져 온다 — 이력은 행(job)당 한 줄, 매수는 r.Copies 로 남긴다
        IEnumerable<(string JobId, ExportResult Result)> results = res.OnPaper
            ? res.Results.GroupBy(x => x.JobId).Select(g =>
                (g.Key, g.All(x => x.Result.Ok) ? g.First().Result : g.First(x => !x.Result.Ok).Result))
            : res.Results;
        foreach (var (jobId, er) in results)
        {
            if (!byId.TryGetValue(jobId, out var r)) continue;
            var f = r.Fields ?? new Fields();
            History.Append(new HistoryEntry
            {
                Item = r.Item, Ref = f.Get("REF"), Lot = r.Lot, Sn = r.Sn, Mfg = r.Mfg, Exp = f.Get("EXP"), Udi = f.Get("UDI_FULL"),
                Copies = Math.Max(1, r.Copies), FileName = er.FileName ?? res.FileName ?? "", Ok = er.Ok, Error = er.Error,
                Mode = mode, Profile = ProfileKey,
            });
        }
    }

    /// <summary>ZEBRA: RenderToBitmap(프린터 dpi) → Monochrome → ZplBuilder → SanityCheck → ConfirmZebraAsync → IZplTransport.SendAsync.</summary>
    private async Task PrintZebraAsync(bool queue)
    {
        var P = Settings.Printer;
        var dpi = P.Dpi > 0 ? P.Dpi : 203;
        List<QueueRow>? rows = null;
        if (queue)
        {
            rows = Queue.Rows.Where(r => r.Status != "done" && r.Status != "error").ToList();
            if (rows.Count == 0) { Toast("출력할 행이 없습니다.", ToastLevel.Warn); return; }
        }
        else
        {
            await LoadSlotImagesAsync(false);
            RenderChecks();
            if ((LastPreflight?.Errors.Count ?? 0) > 0) { Toast("오류를 해결해야 출력할 수 있습니다.", ToastLevel.Err); return; }
        }
        var jobCount = rows?.Count ?? 1;

        // 첫 건으로 ZPL을 만들어 크기·전송량을 확인시킨다
        SetStatus("ZPL 만드는 중…");
        ZplJob first;
        try
        {
            var ctx = MakeRenderContext(Fields, Row, forExport: true);
            var qty = queue ? 1 : Math.Max(1, Copies);
            first = await Task.Run(() => PrinterService.Build(Template, ctx, P, qty));
        }
        catch (Exception ex)
        {
            AppLog.Error("ZPL 생성 실패", ex);
            SetStatus("ZPL 생성 실패: " + ex.Message, StatusLevel.Error);
            Toast("ZPL 생성 실패: " + ex.Message, ToastLevel.Err);
            return;
        }
        var issues = ZplBuilder.SanityCheck(first.Zpl, first.WidthDots, first.HeightDots, dpi, Template.Label);
        var info = new ZebraJobInfo(dpi, first.WidthDots, first.HeightDots, Template.Label.W, Template.Label.H,
                                    PrinterService.TransportLabel(P), first.Zpl.Length);
        if (!await ConfirmZebraAsync(info, issues, jobCount, P)) return;

        // 전송 경로
        IZplTransport? transport;
        try
        {
            transport = PrinterService.CreateTransport(P, this, $"label_{DateTime.Now:yyyy-MM-dd}.zpl");
            if (transport is null) { SetStatus("ZPL 파일 저장을 취소했습니다."); return; }
            if (P.Method != "file") SetStatus($"프린터 연결: {transport.Name}");
        }
        catch (Exception ex)
        {
            AppLog.Error("프린터 연결 실패", ex);
            SetStatus("프린터 연결 실패: " + ex.Message, StatusLevel.Error);
            Toast(ex.Message, ToastLevel.Err);
            return;
        }

        SetBusy(true);
        UpdatePrintButton();
        var zebraCts = new CancellationTokenSource();
        _zebraCts = zebraCts;
        var zct = zebraCts.Token;
        int done = 0, failed = 0;
        try
        {
            if (rows is null)
            {
                try
                {
                    await transport.SendAsync(first.Zpl, CancellationToken.None);
                    done++;
                    AppendZebraHistory(Fields, Math.Max(1, Copies), true, null, transport.Name);
                    SetStatus($"1/1 전송 완료");
                }
                catch (Exception ex)
                {
                    failed++;
                    AppendZebraHistory(Fields, Math.Max(1, Copies), false, ex.Message, transport.Name);
                    SetStatus("전송 실패: " + ex.Message, StatusLevel.Error);
                }
            }
            else
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    if (zct.IsCancellationRequested) break;
                    var r = rows[i];
                    Fields f = r.Fields ?? new Fields();
                    try
                    {
                        f = FieldsOf(r, out var row);
                        r.Fields = f; r.Row = row;
                        await LoadSlotImagesIntoAsync(Template.Objects, f, row, true);   // ★ 이 건의 제품 그림으로 교체
                        var ctx = MakeRenderContext(f, row, forExport: true);
                        var copies = Math.Max(1, r.Copies);
                        var job = await Task.Run(() => PrinterService.Build(Template, ctx, P, copies));
                        Queue.Update(r.Id, x => x.Status = "running");
                        RenderQueue();
                        await transport.SendAsync(job.Zpl, CancellationToken.None);
                        done++;
                        Queue.Update(r.Id, x => { x.Status = "done"; x.FileName = "ZPL 전송"; x.Error = ""; });
                        RenderQueue();
                        AppendZebraHistory(f, copies, true, null, transport.Name);
                        SetStatus($"{i + 1}/{rows.Count} 전송 완료");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Queue.Update(r.Id, x => { x.Status = "error"; x.Error = ex.Message; });
                        RenderQueue();
                        AppendZebraHistory(f, Math.Max(1, r.Copies), false, ex.Message, transport.Name);
                        SetStatus("전송 실패: " + ex.Message, StatusLevel.Error);
                    }
                    await Task.Yield();
                }
            }
        }
        finally
        {
            _zebraCts = null;
            zebraCts.Dispose();
            SetBusy(false);
            await LoadSlotImagesAsync(true);        // 화면을 현재 작업 건으로 되돌린다
            Editor.Invalidate();
        }

        if (P.Method == "file")
        {
            Toast($"ZPL {done}건을 파일로 저장했습니다.", ToastLevel.Ok);
            SetStatus($"ZPL 파일 저장 완료 — {done}건");
        }
        else
        {
            Toast($"전송 완료 {done}건{(failed > 0 ? $" / 실패 {failed}건" : "")}", failed > 0 ? ToastLevel.Warn : ToastLevel.Ok);
            SetStatus($"ZEBRA 출력 완료: 성공 {done}건{(failed > 0 ? $", 실패 {failed}건" : "")}");
        }
        UpdatePrintButton();
    }

    private void AppendZebraHistory(Fields f, int copies, bool ok, string? error, string transportName)
    {
        History.Append(new HistoryEntry
        {
            Item = f.Get("ITEM"), Ref = f.Get("REF"), Lot = f.Get("LOT"), Sn = f.Get("SN"), Mfg = f.Get("MFG"), Exp = f.Get("EXP"),
            Udi = f.Get("UDI_FULL"), Copies = copies, FileName = transportName, Ok = ok, Error = error, Mode = "zebra", Profile = ProfileKey,
        });
    }

    /// <summary>PDF 출력 확인 (PrintConfirmDialog). 진행이면 true.</summary>
    private Task<bool> ConfirmDialogAsync(IReadOnlyList<ExportJob> jobs, PreflightResult pf, bool queueMode = false, int total = 0, int skipped = 0)
        => PrintConfirmDialog.ShowAsync(this, jobs, pf, MakeExportOptions(), queueMode, total, skipped);

    /// <summary>ZEBRA 전송 확인 (ZebraConfirmDialog). 보내면 true.</summary>
    private Task<bool> ConfirmZebraAsync(ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)
        => ZebraConfirmDialog.ShowAsync(this, info, issues, jobCount, printer);

    /* ---- 이미지 슬롯 (★ 행마다 다시 읽는다) ---- */

    /// <summary>현재 서식의 이미지 슬롯을 현재 Fields/Row 로 읽는다 → 바뀌었으면 Editor.Invalidate + RenderChecks.</summary>
    private async Task LoadSlotImagesAsync(bool force)
    {
        bool changed;
        try { changed = await LoadSlotImagesIntoAsync(Template.Objects, Fields, Row, force); }
        catch (Exception ex)
        {
            AppLog.Warn("이미지 슬롯 읽기 실패: " + ex.Message);
            return;
        }
        if (changed)
        {
            Editor.Invalidate();
            RenderChecks();
            RefreshInspector();
            ScheduleSave();
        }
    }

    /// <summary>SlotLoader.LoadIntoAsync(objs, f, row, Images, force, Settings.Imaging…). 하나라도 바뀌었으면 true.</summary>
    private async Task<bool> LoadSlotImagesIntoAsync(IEnumerable<LabelObject> objs, Fields f, DbRow? row, bool force)
    {
        var store = Images;
        var auto = Settings.Imaging.AutoTransparent;
        var tol = Settings.Imaging.Tolerance;
        var list = objs as IReadOnlyList<LabelObject> ?? objs.ToList();
        await _slotGate.WaitAsync();
        try
        {
            return await Task.Run(() => SlotLoader.LoadIntoAsync(list, f, row, store, force, auto, tol));
        }
        finally { _slotGate.Release(); }
    }

    /* ---- 용지 배치 ---- */

    /// <summary>현재 배치 옵션 (Settings.Layout).</summary>
    private LayoutOptions LayoutOpt() => Settings.Layout;

    /// <summary>배치 옵션을 고쳐 저장하고 SyncPaperUi (app.js putLayout).</summary>
    private void PutLayout(Action<LayoutOptions> patch)
    {
        patch(Settings.Layout);
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
        SyncPaperUi();
        RenderChecks();
        UpdatePrintButton();
    }

    /// <summary>selPaper 선택 · printLayoutHint(Paper.Describe) (app.js syncPaperUi).</summary>
    private void SyncPaperUi()
    {
        _syncingPaper = true;
        try { SelectByTag(selPaper, LayoutOpt().Paper); }
        finally { _syncingPaper = false; }
        var txt = DescribeLayout();
        printLayoutHint.Text = txt;
        printLayoutHint.Foreground = txt.StartsWith("❌") ? Res<Brush>("FailBrush") : Res<Brush>("Ink3Brush");
    }

    /// <summary>배치 요약 문구. Core 가 실패하면 상태 대신 빈 문구.</summary>
    internal string DescribeLayout()
    {
        try { return Paper.Describe(Template.Label, LayoutOpt()); }
        catch (Exception ex)
        {
            AppLog.Warn("배치 설명 실패: " + ex.Message);
            return LayoutOpt().Paper == "label" ? "라벨 실물 크기" : "";
        }
    }

    private Task OpenPaperDialogAsync() => OpenPaperDialogAsync(this);

    /// <summary>owner 를 지정하는 판 — 설정 창 위에서 열 때.</summary>
    private async Task OpenPaperDialogAsync(Window owner)
    {
        var before = System.Text.Json.JsonSerializer.Serialize(LayoutOpt());
        var ok = await PaperDialog.ShowAsync(owner, Template.Label, LayoutOpt());
        if (!ok) { SyncPaperUi(); return; }
        PutLayout(_ => { });
        if (LayoutOpt().Paper == "label" && before != System.Text.Json.JsonSerializer.Serialize(LayoutOpt()))
            SetStatus("인쇄 크기를 라벨 실물 크기로 되돌렸습니다.");
        else
            SetStatus("용지 배치를 적용했습니다 — " + DescribeLayout());
    }

    /// <summary>A3 원판에서 라벨 한 장 떼어내기 — 원판 확인 → ExtractDialog → SheetExtractor → InstallTemplate (app.js openExtractDialog).</summary>
    private Task OpenExtractDialogAsync() => OpenExtractDialogAsync(this);

    /// <summary>owner 를 지정하는 판 — 설정 창 위에서 열 때.</summary>
    private async Task OpenExtractDialogAsync(Window owner)
    {
        var isSheet = Math.Abs(Template.Label.W - Paper.Sheet.W) < 2 && Math.Abs(Template.Label.H - Paper.Sheet.H) < 2;
        if (!isSheet)
        {
            var go = await Dialogs.Confirm(owner, "지금 서식은 A3 원판(297×420mm)이 아닙니다. 원판을 불러온 뒤 떼어낼까요?",
                "원판에서 라벨 떼어내기", "원판 불러오기", "취소",
                detail: "현재 배치한 객체는 원판 서식으로 바뀝니다. 되돌리려면 Ctrl+Z 를 누르세요.");
            if (!go) return;
            try { await ApplyPresetTemplateAsync("SHEET-A3"); }
            catch (Exception ex) { Toast("원판 서식을 불러오지 못했습니다: " + ex.Message, ToastLevel.Err); return; }
        }
        var choice = await ExtractDialog.ShowAsync(owner, Template);
        if (choice is null) return;
        try
        {
            SetStatus("라벨을 떼어내는 중…");
            var preset = choice.Preset;
            (LabelTemplate tpl, int taken, int partial, int dropped) r;
            if (preset.Src is { } src)
            {
                var rect = new SKRect((float)src.X, (float)src.Y, (float)(src.X + preset.W), (float)(src.Y + preset.H));
                r = SheetExtractor.Extract(Template, rect, choice.IncludePartial, choice.KeepBackground);
            }
            else r = SheetExtractor.ExtractPreset(Template, preset, choice.KeepBackground);
            InstallTemplate(r.tpl.Label, r.tpl.Objects, preset.Name);
            await LoadSlotImagesAsync(true);
            Toast($"{preset.Name} — 객체 {r.taken}개를 가져왔습니다.", ToastLevel.Ok);
            SetStatus($"{preset.Name} ({r.tpl.Label.W}×{r.tpl.Label.H}mm) 서식으로 바꿨습니다. 객체 {r.taken}개, 남긴 것 {r.dropped}개.");
        }
        catch (Exception ex)
        {
            AppLog.Error("떼어내기 실패", ex);
            SetStatus("떼어내기 실패: " + ex.Message, StatusLevel.Error);
            Toast("떼어내기 실패: " + ex.Message, ToastLevel.Err);
        }
    }

    /* ---- 서식 ---- */

    /// <summary>A3 원판 서식 원본 (객체·배경을 매번 새로 읽는다) (app.js sheetTemplate).</summary>
    private static LabelTemplate SheetTemplate()
    {
        var t = TemplateJson.Default();
        t.Label.BgInclude = true;
        return t;
    }

    /// <summary>프리셋(라벨 규격 id 또는 "SHEET-A3")으로 서식 교체 (app.js applyPresetTemplate).</summary>
    private async Task ApplyPresetTemplateAsync(string presetId)
    {
        var sheet = SheetTemplate();
        if (presetId == "SHEET-A3")
        {
            InstallTemplate(sheet.Label, sheet.Objects, SheetTplName);
            await LoadSlotImagesAsync(true);
            SetStatus($"{SheetTplName} 서식을 적용했습니다 (297×420mm).");
            return;
        }
        var preset = Paper.ProductLabels.FirstOrDefault(p => p.Id == presetId)
                     ?? Paper.CommonLabels.FirstOrDefault(p => p.Id == presetId)
                     ?? Paper.ProductLabels[0];
        var (tpl, taken, _, _) = SheetExtractor.ExtractPreset(sheet, preset, keepBackground: true);
        InstallTemplate(tpl.Label, tpl.Objects, preset.Name);
        await LoadSlotImagesAsync(true);
        SetStatus($"{preset.Name} 서식을 적용했습니다 ({tpl.Label.W}×{tpl.Label.H}mm, 객체 {taken}개).");
    }

    /// <summary>기본 서식(Settings.Template.Preset)으로 되돌린다. confirm 이면 파괴적 확인 먼저 (app.js applyDefaultTemplate).</summary>
    private async Task ApplyDefaultTemplateAsync(bool confirm = true)
    {
        var id = string.IsNullOrWhiteSpace(Settings.Template.Preset) ? "PMFL-001" : Settings.Template.Preset;
        if (confirm && Template.Objects.Count > 0)
        {
            var ok = await Dialogs.Confirm(this, "현재 레이아웃을 기본 서식으로 되돌릴까요?", "서식 되돌리기", "되돌리기", "취소", danger: true,
                detail: "현재 배치한 객체는 사라집니다. 되돌린 뒤 Ctrl+Z로 취소할 수 있습니다.");
            if (!ok) return;
        }
        try { await ApplyPresetTemplateAsync(id); }
        catch (Exception ex)
        {
            AppLog.Error("기본 서식 적용 실패", ex);
            SetStatus("기본 서식 적용 실패: " + ex.Message, StatusLevel.Error);
        }
    }

    /// <summary>Template 의 라벨·객체·이름을 교체하고 편집기·이력·크기 UI 를 맞춘다 (app.js installTemplate).</summary>
    private void InstallTemplate(LabelSize label, IEnumerable<LabelObject> objects, string name)
    {
        Template.Label = new LabelSize { W = label.W, H = label.H, Bg = label.Bg ?? "", BgInclude = label.BgInclude };
        var list = objects.ToList();
        Template.Objects.Clear();
        Template.Objects.AddRange(list);
        for (var i = 0; i < Template.Objects.Count; i++) TemplateJson.Normalize(Template.Objects[i], i);
        TemplateName = name ?? "";
        LayoutDirty = false;
        Editor.Template = Template;
        Editor.Context.Objects = Template.Objects;
        Editor.Context.Background = BgBitmap();
        Editor.Context.IncludeBg = Settings.Output.IncludeBg && Template.Label.BgInclude;
        Editor.Select(Array.Empty<string>(), silent: true);
        Editor.ResetHistory();
        SyncInputsToUi();
        Refresh();
        Editor.ZoomFit();
        if (IsLoaded) Run(RefreshTemplateListAsync);
    }

    /// <summary>selTemplate 채우기 (Templates.List + 프리셋) (app.js refreshTemplateList).</summary>
    private async Task RefreshTemplateListAsync()
    {
        IReadOnlyList<(string Name, DateTime At)> list;
        try { list = await Task.Run(() => Templates.List()); }
        catch (Exception ex) { AppLog.Warn("서식 목록 읽기 실패: " + ex.Message); list = Array.Empty<(string, DateTime)>(); }

        _fillingTemplates = true;
        try
        {
            selTemplate.Items.Clear();
            selTemplate.Items.Add(GroupHeader("기본 제공 (라벨 1장)"));
            foreach (var p in Paper.ProductLabels)
                selTemplate.Items.Add(new ComboBoxItem { Content = $"{p.Name} — {p.W}×{p.H}", Tag = PresetPrefix + p.Id });
            selTemplate.Items.Add(GroupHeader("원본"));
            selTemplate.Items.Add(new ComboBoxItem { Content = $"{SheetTplName} — 297×420", Tag = PresetPrefix + "SHEET-A3" });
            if (list.Count > 0)
            {
                selTemplate.Items.Add(GroupHeader("내가 저장한 서식"));
                foreach (var t in list)
                    selTemplate.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Name, ToolTip = $"저장 {t.At:yyyy-MM-dd HH:mm}" });
            }

            string? tag = null;
            if (list.Any(t => t.Name == TemplateName)) tag = TemplateName;
            else
            {
                var m = Paper.ProductLabels.FirstOrDefault(p => p.Name == TemplateName);
                tag = m is not null ? PresetPrefix + m.Id : (TemplateName == SheetTplName ? PresetPrefix + "SHEET-A3" : null);
            }
            selTemplate.SelectedItem = null;
            if (tag is not null) SelectByTag(selTemplate, tag);
        }
        finally { _fillingTemplates = false; }
    }

    private ComboBoxItem GroupHeader(string text) => new()
    {
        Content = text, IsEnabled = false, FontWeight = FontWeights.Bold, FontSize = 11,
        Foreground = Res<Brush>("Ink3Brush"),
    };

    /// <summary>Ctrl+S / btnTplSave — 이름 입력(Prompt) → Templates.Save → LayoutDirty=false (app.js saveTemplate).</summary>
    private async Task SaveTemplateAsync()
    {
        var isPreset = Paper.MatchPreset(Template.Label.W, Template.Label.H) is not null
                       && (Paper.ProductLabels.Any(p => p.Name == TemplateName) || TemplateName == SheetTplName);
        var name = await Dialogs.Prompt(this, "서식 이름을 입력하세요.", "서식 저장",
            value: isPreset ? "" : TemplateName, placeholder: "예: PML-001 A3 세트");
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var exists = Templates.List().Any(t => t.Name == name);
        if (exists)
        {
            var ok = await Dialogs.Confirm(this, $"\"{name}\" 서식이 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "덮어쓰기", "취소", danger: true);
            if (!ok) return;
        }
        Template.Name = name;
        Templates.Save(name, Template);
        TemplateName = name;
        LayoutDirty = false;
        await RefreshTemplateListAsync();
        RenderChecks();
        UpdatePrintButton();
        ScheduleSave();
        Toast($"서식 \"{name}\" 저장됨", ToastLevel.Ok);
        SetStatus($"서식 저장: {name}");
    }

    /// <summary>selTemplate 선택 → Templates.Load → InstallTemplate (app.js loadTemplate).</summary>
    private async Task LoadTemplateAsync(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (name.StartsWith(PresetPrefix, StringComparison.Ordinal))
        {
            var id = name[PresetPrefix.Length..];
            try { await ApplyPresetTemplateAsync(id); }
            catch (Exception ex)
            {
                AppLog.Error("서식 적용 실패", ex);
                SetStatus("서식 적용 실패: " + ex.Message, StatusLevel.Error);
            }
            await RefreshTemplateListAsync();
            return;
        }
        var t = Templates.Load(name);
        if (t is null) { SetStatus($"서식을 읽지 못했습니다: {name}", StatusLevel.Warn); return; }
        InstallTemplate(t.Label, t.Objects, name);
        await LoadSlotImagesAsync(true);
        SetStatus($"서식 불러옴: {name}");
    }

    private Task ManageTemplatesAsync() => TemplateManageDialog.ShowAsync(this, Templates, RefreshTemplateListAsync);

    /* ---- 잠금 · 실물 보기 ---- */

    /// <summary>Locked, btnLock("🔒 잠금"/"✎ 편집"), statusLock("🔒 잠금"/"✎ 편집 가능"), tgrpInsert/tgrpArrange 비활성, Editor.Locked, 상태줄 (app.js setLocked).</summary>
    private void SetLocked(bool v, bool silent = false)
    {
        Locked = v;
        btnLock.IsChecked = v;
        btnLock.Content = v ? "🔒 잠금" : "✎ 편집";
        statusLock.Text = v ? "🔒 잠금" : "✎ 편집 가능";
        statusLock.Foreground = v ? Res<Brush>("InkBrush") : Res<Brush>("WarnBrush");
        tgrpInsert.IsEnabled = !v;
        tgrpArrange.IsEnabled = !v;
        Editor.Locked = v;
        if (v) Editor.Select(Array.Empty<string>());
        Editor.Invalidate();
        if (!silent)
        {
            SetStatus(v ? "레이아웃이 잠겼습니다. 객체를 옮길 수 없습니다."
                        : "레이아웃 편집이 가능합니다. 변경은 서식으로 저장해야 유지됩니다.",
                v ? StatusLevel.Info : StatusLevel.Warn);
        }
        ScheduleSave();
    }

    /// <summary>F5 / btnPreview — Preview, Editor.PreviewMode, 상태줄 (app.js setPreview).</summary>
    private void TogglePreview()
    {
        Preview = !Preview;
        btnPreview.IsChecked = Preview;
        Editor.PreviewMode = Preview;
        Editor.Invalidate();
        SetStatus(Preview ? "실물 미리보기 — 편집 보조선을 숨겼습니다. (F5로 복귀)" : "편집 화면으로 돌아왔습니다.");
    }

    /* ---- 핸들러 ---- */

    private void OnTemplateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingTemplates || !IsLoaded) return;
        var tag = TagOf(selTemplate);
        if (string.IsNullOrEmpty(tag)) return;
        // 이미 그 서식이면 다시 읽지 않는다 (목록 갱신으로 선택이 되돌아올 때)
        var current = Templates.List().Any(t => t.Name == TemplateName) ? TemplateName
            : Paper.ProductLabels.FirstOrDefault(p => p.Name == TemplateName) is { } m ? PresetPrefix + m.Id
            : TemplateName == SheetTplName ? PresetPrefix + "SHEET-A3" : null;
        if (tag == current) return;
        Run(() => LoadTemplateAsync(tag));
    }

    private void OnTplSaveClick(object sender, RoutedEventArgs e) => Run(SaveTemplateAsync);
    private void OnTplManageClick(object sender, RoutedEventArgs e) => Run(ManageTemplatesAsync);
    private void OnLockClick(object sender, RoutedEventArgs e) => SetLocked(!Locked);
    private void OnPreviewClick(object sender, RoutedEventArgs e) => TogglePreview();

    /// <summary>selTarget → Settings.Output.Target 저장, UpdatePrintButton.</summary>
    private void OnTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var v = TagOf(selTarget);
        if (string.IsNullOrEmpty(v) || v == Settings.Output.Target) return;
        Settings.Output.Target = v;
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
        UpdatePrintButton();
    }

    private void OnPrinterSetupClick(object sender, RoutedEventArgs e) => Run(() => OpenSettingsAsync("printer"));

    /// <summary>selPaper — "custom" 이면 PutLayout + OpenPaperDialogAsync, 아니면 PutLayout + 상태줄("라벨 실물 크기로 1장씩 출력합니다." / Paper.Describe).</summary>
    private void OnPaperChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPaper || !IsLoaded) return;
        var v = TagOf(selPaper);
        if (string.IsNullOrEmpty(v) || v == LayoutOpt().Paper) return;
        if (v == "custom")
        {
            PutLayout(l => l.Paper = "custom");
            Run(OpenPaperDialogAsync);
            return;
        }
        PutLayout(l => l.Paper = v);
        SetStatus(v == "label" ? "라벨 실물 크기로 1장씩 출력합니다." : DescribeLayout());
    }

    private void OnPaperSetupClick(object sender, RoutedEventArgs e) => Run(OpenPaperDialogAsync);
    private void OnPrintClick(object sender, RoutedEventArgs e) => Run(DoPrintSingleAsync);
    private void OnPrintQueueClick(object sender, RoutedEventArgs e) => Run(DoPrintQueueAsync);
}
