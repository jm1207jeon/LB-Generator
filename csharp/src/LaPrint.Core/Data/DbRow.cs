// 라벨DB 한 행과 읽어 온 DB 전체 — 열 문자("A".."BC") → 표시 문자열 (data.js parseWorkbook).
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;

namespace LaPrint.Core.Data;

/// <summary>라벨DB 한 행. 열 문자 → 셀 표시 문자열(trim).</summary>
public sealed class DbRow : Dictionary<string, string>
{
    /// <summary>열 값. 없는 열은 "" (JS 의 r[col] == null → '').</summary>
    public string Get(string? col)
        => col is not null && TryGetValue(col, out var v) ? v : "";
}

/// <summary>읽어 온 라벨DB. Sheets 는 (시트 이름, 키 열 데이터 수).</summary>
public sealed record LabelDb(List<DbRow> Rows, string Sheet, IReadOnlyList<(string Name, int Count)> Sheets,
                             DbRow? Header, int ColCount);

/// <summary>NPOI 로 .xls/.xlsx/.xlsm/.csv 를 읽는다. 셀 값은 표시 문자열 그대로(앞자리 0 보존).</summary>
public static class LabelDbLoader
{
    // 머리글 행 감지: 키 열에 'Product Number' 류 텍스트가 있으면 첫 줄은 머리글
    private static readonly Regex HeaderRe = new(@"product\s*number|품목\s*번호|품번", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>시트 이름과 무관하게 키 열 데이터가 가장 많은 시트를 고른다 ('라벨DB' 우선). 없으면 예외.</summary>
    public static LabelDb Load(Stream s, string fileName, string keyCol)
    {
        var key = (string.IsNullOrWhiteSpace(keyCol) ? "H" : keyCol.Trim()).ToUpperInvariant();
        var sheets = new List<(string Name, int Count)>();
        string? best = null;
        List<Dictionary<string, string>>? bestRows = null;
        Dictionary<string, string>? bestHeader = null;
        double bestScore = -1;

        foreach (var (name, raw) in ReadSheets(s, fileName))
        {
            if (raw.Count == 0) { sheets.Add((name, 0)); continue; }
            var headerish = HeaderRe.IsMatch(raw[0].TryGetValue(key, out var h0) ? h0 : "");
            var header = headerish ? raw[0] : null;
            var data = raw.Skip(headerish ? 1 : 0)
                .Where(r => r.TryGetValue(key, out var kv) && kv.Trim() != "")
                .ToList();
            sheets.Add((name, data.Count));
            var score = data.Count + (name == "라벨DB" ? 1e9 : 0);
            if (score > bestScore) { bestScore = score; best = name; bestRows = data; bestHeader = header; }
        }
        if (bestRows is null || bestRows.Count == 0 || best is null)
        {
            var detail = string.Join(", ", sheets.Select(x => $"{x.Name}({x.Count})"));
            throw new InvalidDataException($"{key}열(품목번호)에서 데이터를 찾지 못했습니다. "
                + $"시트: {(detail.Length > 0 ? detail : "없음")} — 설정 › 데이터 매칭에서 품목번호 열을 바꿀 수 있습니다.");
        }

        var rows = bestRows.Select(Norm).ToList();
        // 실제로 값이 있는 마지막 열
        var maxIdx = 0;
        foreach (var r in rows.Take(300))
            foreach (var kv in r)
                if (kv.Value != "") maxIdx = Math.Max(maxIdx, ColumnName.ToIndex(kv.Key));
        if (bestHeader is not null)
            foreach (var kv in bestHeader)
                if (kv.Value.Trim() != "") maxIdx = Math.Max(maxIdx, ColumnName.ToIndex(kv.Key));

        return new LabelDb(rows, best, sheets, bestHeader is null ? null : Norm(bestHeader), maxIdx + 1);
    }

    private static DbRow Norm(Dictionary<string, string> r)
    {
        var o = new DbRow();
        foreach (var kv in r) o[kv.Key] = kv.Value.Trim();
        return o;
    }

    /* ---------------- 시트 읽기 ---------------- */

    private static IEnumerable<(string Name, List<Dictionary<string, string>> Rows)> ReadSheets(Stream s, string fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (ext is ".csv" or ".txt" or ".tsv")
            return new[] { ("Sheet1", ReadCsv(s, ext == ".tsv" ? '\t' : ',')) };
        return ReadWorkbook(s);
    }

    private static IEnumerable<(string Name, List<Dictionary<string, string>> Rows)> ReadWorkbook(Stream s)
    {
        Stream src = s;
        if (!s.CanSeek)
        {
            var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            src = ms;
        }
        var wb = WorkbookFactory.Create(src);
        var fmt = new DataFormatter(CultureInfo.InvariantCulture);
        var list = new List<(string, List<Dictionary<string, string>>)>();
        for (var i = 0; i < wb.NumberOfSheets; i++)
        {
            var sheet = wb.GetSheetAt(i);
            list.Add((sheet.SheetName, ReadSheet(sheet, fmt)));
        }
        return list;
    }

    private static List<Dictionary<string, string>> ReadSheet(ISheet sheet, DataFormatter fmt)
    {
        var rows = new List<Dictionary<string, string>>();
        if (sheet.PhysicalNumberOfRows == 0) return rows;
        // SheetJS 처럼 시트 범위 안의 모든 열을 키로 채운다 (빈 셀은 "")
        var maxCol = 0;
        for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is not null) maxCol = Math.Max(maxCol, row.LastCellNum);
        }
        if (maxCol <= 0) return rows;
        var names = new string[maxCol];
        for (var c = 0; c < maxCol; c++) names[c] = ColumnName.FromIndex(c);

        for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;
            var o = new Dictionary<string, string>(maxCol);
            var isEmpty = true;
            for (var c = 0; c < maxCol; c++)
            {
                var cell = row.GetCell(c);
                if (cell is null || cell.CellType == CellType.Blank) { o[names[c]] = ""; continue; }
                o[names[c]] = CellText(cell, fmt);
                isEmpty = false;
            }
            // 값이 하나도 없는 줄은 건너뛴다 (sheet_to_json 의 blankrows 기본값)
            if (isEmpty) continue;
            rows.Add(o);
        }
        return rows;
    }

    /// <summary>셀의 표시 문자열. 숫자는 정수면 소수점 없이, 수식은 캐시된 결과를 쓴다.</summary>
    private static string CellText(ICell cell, DataFormatter fmt)
    {
        try
        {
            switch (cell.CellType)
            {
                case CellType.String: return cell.StringCellValue ?? "";
                case CellType.Boolean: return cell.BooleanCellValue ? "TRUE" : "FALSE";
                case CellType.Error: return ErrorText(cell.ErrorCellValue);
                case CellType.Numeric: return NumericText(cell, cell.NumericCellValue, fmt);
                case CellType.Formula:
                    switch (cell.CachedFormulaResultType)
                    {
                        case CellType.String: return cell.StringCellValue ?? "";
                        case CellType.Boolean: return cell.BooleanCellValue ? "TRUE" : "FALSE";
                        case CellType.Error: return ErrorText(cell.ErrorCellValue);
                        case CellType.Numeric: return NumericText(cell, cell.NumericCellValue, fmt);
                        default: return "";
                    }
                default: return "";
            }
        }
        catch
        {
            return "";
        }
    }

    private static string NumericText(ICell cell, double v, DataFormatter fmt)
    {
        var style = cell.CellStyle;
        var idx = style?.DataFormat ?? 0;
        var fs = style?.GetDataFormatString() ?? "General";
        if (idx == 0 || string.IsNullOrEmpty(fs) || fs == "General" || fs == "@") return GeneralText(v);
        try
        {
            var t = fmt.FormatRawCellContents(v, idx, fs);
            return string.IsNullOrEmpty(t) ? GeneralText(v) : t;
        }
        catch
        {
            return GeneralText(v);
        }
    }

    // 엑셀 '일반' 서식 — 정수는 소수점 없이(6 → "6"), 그 밖은 소수 10자리까지
    private static string GeneralText(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "";
        if (Math.Abs(v) < 1e15 && v == Math.Floor(v)) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static string ErrorText(byte code)
    {
        try { return FormulaError.ForInt(code).String; }
        catch { return "#ERR"; }
    }

    /* ---------------- CSV ---------------- */

    private static List<Dictionary<string, string>> ReadCsv(Stream s, char sep)
    {
        using var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = reader.ReadToEnd();
        var records = ParseCsv(text, sep);
        var maxCol = records.Count == 0 ? 0 : records.Max(r => r.Count);
        var rows = new List<Dictionary<string, string>>();
        foreach (var rec in records)
        {
            if (rec.All(v => v.Length == 0)) continue;
            var o = new Dictionary<string, string>(maxCol);
            for (var c = 0; c < maxCol; c++) o[ColumnName.FromIndex(c)] = c < rec.Count ? rec[c] : "";
            rows.Add(o);
        }
        return rows;
    }

    private static List<List<string>> ParseCsv(string text, char sep)
    {
        var records = new List<List<string>>();
        var rec = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    quoted = false; i++; continue;
                }
                field.Append(ch); i++; continue;
            }
            if (ch == '"' && field.Length == 0) { quoted = true; i++; continue; }
            if (ch == sep) { rec.Add(field.ToString()); field.Clear(); i++; continue; }
            if (ch == '\r' || ch == '\n')
            {
                rec.Add(field.ToString()); field.Clear();
                records.Add(rec); rec = new List<string>();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++; continue;
            }
            field.Append(ch); i++;
        }
        if (field.Length > 0 || rec.Count > 0) { rec.Add(field.ToString()); records.Add(rec); }
        return records;
    }
}
