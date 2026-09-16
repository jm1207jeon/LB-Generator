// 라벨DB 캐시 — cache/<profile>.db.json.gz (gzip JSON + 파일 수정 시각). 수정 시각이 같으면 12,000행 재파싱을 건너뛴다.
using System.IO.Compression;
using System.Text.Json;
using LaPrint.Core.Data;

namespace LaPrint.Core.Storage;

/// <summary>프로필별 라벨DB 파싱 결과 캐시 (cache 폴더).</summary>
public sealed class DbCache
{
    private sealed class SheetDto
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    private sealed class CacheDto
    {
        public long LastWrite { get; set; }
        public string Sheet { get; set; } = "";
        public List<SheetDto> Sheets { get; set; } = new();
        public Dictionary<string, string>? Header { get; set; }
        public int ColCount { get; set; }
        public List<Dictionary<string, string>> Rows { get; set; } = new();
    }

    /// <summary>캐시 파일 경로.</summary>
    public static string FileOf(string profile)
        => Path.Combine(AppPaths.CacheDir, StorageJson.SafeFileName(string.IsNullOrEmpty(profile) ? "general" : profile) + ".db.json.gz");

    public void Put(string profile, LabelDb db, DateTime lastWrite)
    {
        ArgumentNullException.ThrowIfNull(db);
        var dto = new CacheDto
        {
            LastWrite = lastWrite.Ticks,
            Sheet = db.Sheet,
            Sheets = db.Sheets.Select(s => new SheetDto { Name = s.Name, Count = s.Count }).ToList(),
            Header = db.Header,
            ColCount = db.ColCount,
            Rows = db.Rows.Select(r => (Dictionary<string, string>)r).ToList(),
        };
        try
        {
            StorageJson.WriteAtomic(FileOf(profile), fs =>
            {
                using var gz = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: true);
                JsonSerializer.Serialize(gz, dto, StorageJson.Compact);
            });
        }
        catch (Exception ex)
        {
            // 캐시를 못 써도 다음에 다시 파싱하면 되므로 오류로 끝내지 않는다
            AppLog.Warn("라벨DB 캐시를 쓸 수 없습니다: " + FileOf(profile), ex);
        }
    }

    /// <summary>같은 lastWrite 로 저장된 것이 있으면 돌려준다. 없으면 null.</summary>
    public LabelDb? Get(string profile, DateTime lastWrite)
    {
        var path = FileOf(profile);
        if (!File.Exists(path)) return null;
        try
        {
            CacheDto? dto;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                dto = JsonSerializer.Deserialize<CacheDto>(gz, StorageJson.Compact);
            }
            if (dto is null || dto.LastWrite != lastWrite.Ticks) return null;

            var rows = new List<DbRow>(dto.Rows.Count);
            foreach (var d in dto.Rows) rows.Add(ToRow(d));
            var sheets = dto.Sheets.Select(s => (s.Name, s.Count)).ToList();
            return new LabelDb(rows, dto.Sheet, sheets, dto.Header is null ? null : ToRow(dto.Header), dto.ColCount);
        }
        catch (Exception ex)
        {
            AppLog.Warn("라벨DB 캐시를 읽을 수 없습니다: " + path, ex);
            return null;
        }
    }

    /// <summary>캐시 파일을 지운다.</summary>
    public void Invalidate(string profile)
    {
        var path = FileOf(profile);
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Warn("라벨DB 캐시를 지울 수 없습니다: " + path, ex); }
    }

    private static DbRow ToRow(Dictionary<string, string> d)
    {
        var r = new DbRow();
        foreach (var kv in d) r[kv.Key] = kv.Value;
        return r;
    }
}
