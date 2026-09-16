// 연속 작업 큐 — 붙여넣기/파일 파싱, SN 연번 전개, 전체 검증, 실행(일시정지/중지) (batch.js).
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using NPOI.SS.UserModel;

namespace LaPrint.Core.Batch;

/// <summary>큐 검증 요약의 대표 이슈 한 건 (수준·코드별 개수).</summary>
public sealed record QueueIssueCount(string Level, string Code, string Msg, int Count);

/// <summary>큐 전체 검증 요약 (batch.js validateAll 반환값).</summary>
public sealed record QueueSummary(int Total, int ErrorRows, int WarnRows, int OkRows, IReadOnlyList<QueueIssueCount> Issues);

/// <summary>큐 상태와 실행.</summary>
public sealed class QueueEngine
{
    /// <summary>붙여넣기/파일의 열 이름 → 내부 필드 (batch.js COLUMN_ALIASES).</summary>
    public static IReadOnlyDictionary<string, string[]> ColumnAliases { get; } = new Dictionary<string, string[]>
    {
        ["item"] = new[] { "품목번호", "품목", "item", "product number", "productnumber", "pn", "품번", "h" },
        ["lot"] = new[] { "lot", "로트", "로트번호", "lot no", "lotno", "batch" },
        ["sn"] = new[] { "sn", "시리얼", "일련번호", "serial", "serial no" },
        ["mfg"] = new[] { "mfg", "제조일", "제조일자", "manufacture", "manufacturing date", "date of manufacture" },
        ["exp"] = new[] { "exp", "유효일", "유효기간", "use by", "expiry", "expiration" },
        ["months"] = new[] { "개월", "유효기간개월", "months", "유효개월" },
        ["copies"] = new[] { "매수", "수량", "qty", "quantity", "copies", "count" },
    };

    // 머리글이 없을 때의 위치 기준 열 순서
    private static readonly string[] PositionalFields = { "item", "lot", "sn", "mfg", "copies" };
    private static readonly string[] AliasOrder = { "item", "lot", "sn", "mfg", "exp", "months", "copies" };

    private static readonly Regex HeaderStrip = new(@"[\s_()\[\].-]", RegexOptions.Compiled);
    private static readonly Regex Quoted = new("^\"(.*)\"$", RegexOptions.Compiled);
    private static readonly Regex DateSep = new(@"^([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})$", RegexOptions.Compiled);
    private static readonly Regex Date8 = new(@"^([0-9]{4})([0-9]{2})([0-9]{2})$", RegexOptions.Compiled);
    private static readonly Regex Date6 = new(@"^([0-9]{2})([0-9]{2})([0-9]{2})$", RegexOptions.Compiled);
    private static readonly Regex Serial5 = new(@"^[0-9]{5}$", RegexOptions.Compiled);
    private static readonly Regex CsvNeedsQuote = new("[\",\n\t]", RegexOptions.Compiled);

    private readonly List<QueueRow> _rows = new();
    private readonly object _gate = new();
    private volatile bool _cancelRequested;
    private CancellationTokenSource? _runCts;

    public IReadOnlyList<QueueRow> Rows => _rows;
    public int Count => _rows.Count;

    /// <summary>모든 행의 매수 합 (매수가 0 이면 1 로 센다).</summary>
    public int TotalLabels
    {
        get
        {
            var n = 0;
            foreach (var r in _rows) n += CopiesOf(r);
            return n;
        }
    }

    public bool Running { get; private set; }
    public bool Paused { get; private set; }
    public (int Index, int Total) Progress { get; private set; }

    /// <summary>행·상태가 바뀔 때마다.</summary>
    public event Action? Changed;

    /* ---------------- 행 조작 ---------------- */

    public QueueRow Add(QueueRow r)
    {
        ArgumentNullException.ThrowIfNull(r);
        _rows.Add(r);
        RaiseChanged();
        return r;
    }

    public void AddMany(IEnumerable<QueueRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows.AddRange(rows);
        RaiseChanged();
    }

    /// <summary>행을 고친다. 상태를 바꾸지 않은 편집은 (처리중이 아니면) 대기로 되돌린다.</summary>
    public void Update(string id, Action<QueueRow> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var r = Find(id);
        if (r is null) return;
        var before = r.Status;
        edit(r);
        if (r.Status == before && r.Status != "running") r.Status = "pending";
        RaiseChanged();
    }

    public void Remove(string id)
    {
        _rows.RemoveAll(r => r.Id == id);
        RaiseChanged();
    }

    public void Clear()
    {
        _rows.Clear();
        Progress = (0, 0);
        RaiseChanged();
    }

    /// <summary>done/error 를 pending 으로 되돌린다.</summary>
    public void ResetStatus()
    {
        foreach (var r in _rows)
        {
            if (r.Status == "running") continue;
            r.Status = "pending";
            r.FileName = "";
            r.Error = "";
        }
        Progress = (0, 0);
        RaiseChanged();
    }

    /* ---------------- SN 연번 전개 ---------------- */

    /// <summary>SN 연번 전개 (from~to, pad 자리 0 채움). proto 가 큐에 있으면 그 자리를 전개된 행들로 바꾼다.</summary>
    public IEnumerable<QueueRow> ExpandSerial(QueueRow proto, int from, int to, int pad)
    {
        ArgumentNullException.ThrowIfNull(proto);
        if (to < from) throw new ArgumentException("SN 끝 값이 시작 값보다 작습니다.");
        var n = (long)to - from + 1;
        if (n > 2000) throw new ArgumentException($"한 번에 {n}개는 너무 많습니다. 2000개 이하로 나누어 주세요.");
        var width = pad > 0 ? pad : from.ToString(CultureInfo.InvariantCulture).Length;

        var made = new List<QueueRow>((int)n);
        for (var v = from; v <= to; v++)
        {
            var c = proto.CloneInputs();
            c.Sn = v.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            made.Add(c);
        }

        var i = _rows.FindIndex(r => ReferenceEquals(r, proto) || r.Id == proto.Id);
        if (i >= 0)
        {
            _rows.RemoveAt(i);
            _rows.InsertRange(i, made);
            RaiseChanged();
        }
        return made;
    }

    /* ---------------- 붙여넣기 / 파일 ---------------- */

    /// <summary>열 이름을 내부 필드로 (대소문자·공백·구분 기호 무시). 모르면 null.</summary>
    public static string? MatchColumn(string? header)
    {
        var h = NormHeader(header);
        foreach (var field in AliasOrder)
        {
            foreach (var a in ColumnAliases[field])
            {
                if (NormHeader(a) == h) return field;
            }
        }
        return null;
    }

    /// <summary>다양한 날짜 표기를 ISO(YYYY-MM-DD)로. 모르는 표기는 그대로 돌려준다.</summary>
    public static string NormDate(string? v)
    {
        var s = JsTrim(v);
        if (s.Length == 0) return "";
        var m = DateSep.Match(s);
        if (m.Success) return $"{m.Groups[1].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[3].Value.PadLeft(2, '0')}";
        m = Date8.Match(s);
        if (m.Success) return $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
        m = Date6.Match(s);          // YYMMDD
        if (m.Success) return $"20{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
        // 엑셀 날짜 일련번호
        if (Serial5.IsMatch(s))
        {
            var d = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc).AddDays(int.Parse(s, CultureInfo.InvariantCulture));
            return $"{d.Year:0000}-{d.Month:00}-{d.Day:00}";
        }
        return s;
    }

    /// <summary>탭/쉼표 표 텍스트 파싱 — COLUMN_ALIASES 머리글 인식, normDate.</summary>
    public static (List<QueueRow> Rows, Dictionary<string, int> Mapping, bool HeaderDetected, string? Error) ParseTable(string text, QueueRow? defaults)
    {
        var raw = JsTrim((text ?? "").Replace("\r\n", "\n").Replace('\r', '\n'));
        if (raw.Length == 0) return (new List<QueueRow>(), new Dictionary<string, int>(), false, "붙여넣을 내용이 없습니다.");
        var lines = raw.Split('\n').Where(l => JsTrim(l).Length != 0).ToList();
        var delim = lines[0].Contains('\t') ? '\t' : (lines[0].Contains(',') ? ',' : '\t');
        var cells = lines.Select(l => l.Split(delim).Select(c => Quoted.Replace(JsTrim(c), "$1")).ToArray()).ToList();

        // 머리글 감지
        var mapping = new Dictionary<string, int>();
        var headerDetected = false;
        var first = cells[0];
        var matched = first.Select(MatchColumn).ToArray();
        if (matched.Count(f => f is not null) >= Math.Min(2, first.Length))
        {
            headerDetected = true;
            for (var i = 0; i < matched.Length; i++) if (matched[i] is { } f) mapping[f] = i;
        }
        else
        {
            // 머리글이 없으면 위치 기준: 품목번호, LOT, SN, MFG, 매수
            for (var i = 0; i < PositionalFields.Length; i++) if (i < first.Length) mapping[PositionalFields[i]] = i;
        }

        var body = headerDetected ? cells.Skip(1) : cells;
        var outRows = new List<QueueRow>();
        foreach (var c in body)
        {
            string Pick(string f) => mapping.TryGetValue(f, out var ix) && ix < c.Length ? JsTrim(c[ix]) : "";
            var item = Pick("item");
            var lot = Pick("lot");
            if (item.Length == 0 && lot.Length == 0) continue;
            var r = defaults?.CloneInputs() ?? new QueueRow();
            r.Item = item;
            r.Lot = lot;
            r.Sn = Pick("sn");
            r.Mfg = NormDate(Pick("mfg"));
            var copies = JsNumber(Pick("copies"));
            r.Copies = (int)Math.Max(1, double.IsNaN(copies) || copies == 0 ? 1 : copies);
            var exp = NormDate(Pick("exp"));
            var months = JsNumber(Pick("months"));
            if (exp.Length != 0) { r.Exp = exp; r.ExpAuto = false; }
            if (double.IsFinite(months) && months > 0) r.Months = (int)months;
            outRows.Add(r);
        }
        if (outRows.Count == 0) return (outRows, mapping, headerDetected, "인식된 데이터 행이 없습니다. 품목번호 열이 있는지 확인하세요.");
        return (outRows, mapping, headerDetected, null);
    }

    /// <summary>csv/txt/tsv 는 ParseTable, xlsx/xls 는 NPOI 첫 시트.</summary>
    public static Task<(List<QueueRow> Rows, Dictionary<string, int> Mapping, bool HeaderDetected, string? Error)> ParseFileAsync(string path, QueueRow? defaults)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Task.Run(() =>
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            if (name.EndsWith(".csv", StringComparison.Ordinal) || name.EndsWith(".txt", StringComparison.Ordinal) || name.EndsWith(".tsv", StringComparison.Ordinal))
                return ParseTable(ReadTextFile(path), defaults);
            return ParseTable(SheetToTabText(path), defaults);
        });
    }

    /// <summary>큐를 CSV로 (템플릿 배포/재사용용). 엑셀에서 한글이 깨지지 않도록 BOM 을 붙인다.</summary>
    public string ToCsv()
    {
        var head = new[] { "품목번호", "LOT", "SN", "제조일", "유효기간개월", "유효일", "매수" };
        static string Esc(string? v)
        {
            var s = v ?? "";
            return CsvNeedsQuote.IsMatch(s) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        var lines = new List<string> { string.Join(",", head) };
        foreach (var r in _rows)
        {
            lines.Add(string.Join(",", new[]
            {
                r.Item, r.Lot, r.Sn, r.Mfg,
                r.ExpAuto ? r.Months.ToString(CultureInfo.InvariantCulture) : "",
                r.ExpAuto ? "" : r.Exp,
                r.Copies.ToString(CultureInfo.InvariantCulture),
            }.Select(Esc)));
        }
        return "﻿" + string.Join("\r\n", lines);
    }

    /* ---------------- 검증 ---------------- */

    /// <summary>큐 전체를 검증한다. 각 행에 Issues/Status 를 채우고 요약을 돌려준다. ctxOf 는 Fields/Row 가 채워진 행을 받는다.</summary>
    public QueueSummary ValidateAll(LabelIndex index, FieldMap map, LabelTemplate t, ValidationRules rules, double dpi, Func<QueueRow, RenderContext> ctxOf)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(ctxOf);

        var errorRows = 0; var warnRows = 0; var okRows = 0;
        var dupes = new Dictionary<string, int>();
        foreach (var r in _rows)
        {
            var key = DupKey(r);
            dupes[key] = dupes.TryGetValue(key, out var c) ? c + 1 : 1;
        }
        foreach (var r in _rows)
        {
            DbRow? row = null;
            if (index?.ByRef is { } byRef) byRef.TryGetValue(JsTrim(r.Item), out row);
            var fields = FieldComputer.Compute(row, r.ToInputs(), map);
            r.Fields = fields;
            r.Row = row;
            var ctx = ctxOf(r) ?? new RenderContext();
            // 이 행의 데이터로 점검해야 하므로 문맥의 필드·행은 여기서 계산한 것으로 맞춘다
            ctx.Fields = fields;
            ctx.Row = row;
            if (ctx.Objects.Count == 0) ctx.Objects = t.Objects;
            var pf = Preflight.Run(t, ctx, rules, dpi);
            r.Issues = new List<Issue>(pf.All);
            if (pf.Errors.Count > 0) { r.Status = r.Status == "done" ? "done" : "error"; errorRows++; }
            else if (pf.Warnings.Count > 0) { if (r.Status != "done") r.Status = "pending"; warnRows++; }
            else { if (r.Status != "done") r.Status = "pending"; okRows++; }

            if (dupes[DupKey(r)] > 1)
                r.Issues.Add(new Issue("warn", "DUPLICATE", "같은 품목·LOT·SN 조합이 큐에 중복되어 있습니다."));
            r.Error = pf.Errors.Count > 0 ? pf.Errors[0].Msg : "";
        }
        // 대표 이슈 집계
        var byCode = new Dictionary<string, (string Level, string Code, string Msg, int Count)>();
        var order = new List<string>();
        foreach (var r in _rows)
        {
            foreach (var i in r.Issues)
            {
                var k = i.Level + "|" + i.Code;
                if (!byCode.TryGetValue(k, out var e)) { e = (i.Level, i.Code, i.Msg, 0); order.Add(k); }
                byCode[k] = (e.Level, e.Code, e.Msg, e.Count + 1);
            }
        }
        var issues = order.Select(k => byCode[k])
            .OrderBy(e => LevelRank(e.Level))
            .ThenByDescending(e => e.Count)
            .Select(e => new QueueIssueCount(e.Level, e.Code, e.Msg, e.Count))
            .ToList();
        RaiseChanged();
        return new QueueSummary(_rows.Count, errorRows, warnRows, okRows, issues);
    }

    /* ---------------- 실행 ---------------- */

    /// <summary>큐 실행. prepareJob 은 행마다 await 되며 치환기와 이미지 슬롯을 그 행 데이터로 바꿔야 한다.</summary>
    public async Task<BatchResult> RunAsync(LabelTemplate t, ExportOptions o, PdfExporter exporter, Func<QueueRow, Task> prepareJob,
                                            IProgress<(int Index, int Total, QueueRow? Row)>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(prepareJob);

        if (Running) return Fail("이미 실행 중입니다.");
        var targets = _rows.Where(r => r.Status != "done").ToList();
        if (targets.Count == 0) return Fail("출력할 행이 없습니다.");

        var total = targets.Sum(CopiesOf);
        _cancelRequested = false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate) _runCts = cts;
        SetRunning(true, false, (0, total));

        BatchResult result;
        try
        {
            // 행마다 다른 데이터로 그려야 하므로 exporter 에 넘기기 전에 이 행의 데이터를 준비한다
            var jobs = new List<ExportJob>(targets.Count);
            foreach (var r in targets)
            {
                await prepareJob(r).ConfigureAwait(false);
                jobs.Add(new ExportJob(r.Id, CopiesOf(r), r.Fields ?? FallbackFields(r), r.Row, t.Objects));
            }

            var byId = targets.ToDictionary(r => r.Id);
            var adapter = new SyncProgress<(int Index, int Total, ExportJob Job, ExportResult? Res)>(p =>
            {
                Progress = (p.Index, p.Total);
                byId.TryGetValue(p.Job.Id, out var r);
                if (r is not null)
                {
                    if (p.Res is { Ok: false }) { r.Status = "error"; r.Error = p.Res.Error ?? "출력 실패"; }
                    else if (p.Res is not null) { r.Status = "done"; r.FileName = p.Res.FileName ?? ""; }
                    else r.Status = "running";
                }
                progress?.Report((p.Index, p.Total, r));
                RaiseChanged();
            });

            // exportBatch 가 각 job 을 그리기 직전에 부를 훅.
            // 치환기뿐 아니라 ★그림 슬롯도 이 건의 데이터로 다시 읽어야 한다.
            Func<ExportJob, Task> beforeJob = job =>
                byId.TryGetValue(job.Id, out var r) ? prepareJob(r) : Task.CompletedTask;

            result = await exporter.ExportBatch(t, jobs, o, beforeJob, adapter, () => Paused, cts.Token).ConfigureAwait(false);

            // merged 모드에서는 개별 상태가 안 잡히므로 일괄 처리
            if (o.Mode == "merged" && !result.Cancelled)
            {
                foreach (var r in targets) if (r.Status != "error") { r.Status = "done"; r.FileName = result.FileName ?? ""; }
            }
        }
        catch (OperationCanceledException)
        {
            result = new BatchResult(false, 0, 0, true, null, 0, Array.Empty<(string, ExportResult)>());
        }
        finally
        {
            foreach (var r in _rows) if (r.Status == "running") r.Status = _cancelRequested ? "pending" : "done";
            lock (_gate) _runCts = null;
            cts.Dispose();
            SetRunning(false, false, Progress);
        }
        return result;
    }

    public void Pause()
    {
        Paused = true;
        RaiseChanged();
    }

    public void Resume()
    {
        Paused = false;
        RaiseChanged();
    }

    public void Cancel()
    {
        _cancelRequested = true;
        Paused = false;
        CancellationTokenSource? cts;
        lock (_gate) cts = _runCts;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        RaiseChanged();
    }

    /// <summary>중지가 요청되었는지.</summary>
    public bool CancelRequested => _cancelRequested;

    /* ---------------- 내부 ---------------- */

    private QueueRow? Find(string id) => _rows.Find(r => r.Id == id);

    private static int CopiesOf(QueueRow r) => r.Copies == 0 ? 1 : r.Copies;

    private static string DupKey(QueueRow r) => string.Join("|", r.Item, r.Lot, r.Sn);

    private static int LevelRank(string level) => level switch { "error" => 0, "warn" => 1, _ => 2 };

    private static BatchResult Fail(string error)
        => new(false, 0, 0, false, null, 0, Array.Empty<(string, ExportResult)>(), error);

    // 검증 없이 실행된 행 — 입력값만으로 최소 필드를 만든다 (prepareJob 이 Fields 를 채우는 것이 정상)
    private static Fields FallbackFields(QueueRow r)
    {
        var f = new Fields
        {
            ["ITEM"] = r.Item,
            ["LOT"] = r.Lot,
            ["SN"] = r.Sn,
            ["MFG"] = r.Mfg,
            ["EXP"] = r.Exp,
        };
        return f;
    }

    private static string NormHeader(string? h) => HeaderStrip.Replace(JsTrim(h).ToLowerInvariant(), "");

    // JS 의 String.prototype.trim 은 BOM(U+FEFF)도 지우지만 .NET 의 Trim 은 지우지 않는다
    private static string JsTrim(string? s) => (s ?? "").Trim().Trim('﻿').Trim();

    // JS Number(): 빈 문자열은 0, 숫자가 아니면 NaN
    private static double JsNumber(string? s)
    {
        var t = JsTrim(s);
        if (t.Length == 0) return 0;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }

    private static string ReadTextFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // UTF-8 이 아니면 한국어 Windows 기본 코드페이지(CP949)로 본다
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    // SheetJS sheet_to_csv({FS:'\t'}) 와 같은 탭 구분 텍스트 — 첫 시트, 표시 문자열 그대로
    private static string SheetToTabText(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        IWorkbook wb = WorkbookFactory.Create(fs);
        if (wb.NumberOfSheets == 0) return "";
        var ws = wb.GetSheetAt(0);
        var fmt = new DataFormatter(CultureInfo.InvariantCulture);
        var eval = wb.GetCreationHelper().CreateFormulaEvaluator();
        var sb = new StringBuilder();
        var last = ws.LastRowNum;
        for (var ri = 0; ri <= last; ri++)
        {
            var row = ws.GetRow(ri);
            if (ri > 0) sb.Append('\n');
            if (row is null) continue;
            var n = row.LastCellNum;
            for (var ci = 0; ci < n; ci++)
            {
                if (ci > 0) sb.Append('\t');
                var cell = row.GetCell(ci);
                if (cell is null) continue;
                string v;
                try { v = fmt.FormatCellValue(cell, eval); }
                catch (Exception) { v = cell.ToString() ?? ""; }
                if (v.IndexOfAny(new[] { '\t', '\n', '"' }) >= 0) v = "\"" + v.Replace("\"", "\"\"") + "\"";
                sb.Append(v);
            }
        }
        return sb.ToString();
    }

    private void RaiseChanged() => Changed?.Invoke();

    private void SetRunning(bool running, bool paused, (int Index, int Total) progress)
    {
        Running = running;
        Paused = paused;
        Progress = progress;
        RaiseChanged();
    }

    /// <summary>동기화 문맥 없이 즉시 부르는 IProgress.</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
