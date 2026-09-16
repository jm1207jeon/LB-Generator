// 서식 저장소 — templates 폴더에 이름별 JSON 과 index.json {name, file, at} 목록 (store.js templates).
using System.Text.Json;
using LaPrint.Core.Model;

namespace LaPrint.Core.Storage;

/// <summary>저장된 서식 목록·읽기·쓰기·삭제.</summary>
public sealed class TemplateStore
{
    private sealed class IndexEntry
    {
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public DateTime At { get; set; }
    }

    private static string IndexFile => Path.Combine(AppPaths.TemplatesDir, "index.json");

    /// <summary>저장된 순서대로 (이름, 저장 시각).</summary>
    public IReadOnlyList<(string Name, DateTime At)> List()
        => ReadIndex().Select(e => (e.Name, e.At)).ToList();

    /// <summary>이름으로 읽는다. 없거나 깨졌으면 null.</summary>
    public LabelTemplate? Load(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var path = FileOf(name);
        if (!File.Exists(path)) return null;
        try
        {
            return TemplateJson.Load(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            AppLog.Warn("서식 파일을 읽을 수 없습니다: " + path, ex);
            return null;
        }
    }

    /// <summary>같은 이름이 있으면 덮어쓴다.</summary>
    public void Save(string name, LabelTemplate t)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(t);
        name = name.Trim();
        var index = ReadIndex();
        var entry = index.Find(e => e.Name == name);
        if (entry is null)
        {
            entry = new IndexEntry { Name = name, File = UniqueFile(index, StorageJson.SafeFileName(name) + ".json") };
            index.Add(entry);
        }
        entry.At = DateTime.Now;
        StorageJson.WriteAtomic(Path.Combine(AppPaths.TemplatesDir, entry.File), TemplateJson.Save(t));
        WriteIndex(index);
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var index = ReadIndex();
        var entry = index.Find(e => e.Name == name);
        var path = entry is not null ? Path.Combine(AppPaths.TemplatesDir, entry.File) : FileOf(name);
        if (File.Exists(path)) File.Delete(path);
        if (entry is not null)
        {
            index.Remove(entry);
            WriteIndex(index);
        }
    }

    // 색인에 있으면 그 파일, 없으면 안전한 이름으로 짐작한다
    private static string FileOf(string name)
    {
        var entry = ReadIndex().Find(e => e.Name == name);
        return Path.Combine(AppPaths.TemplatesDir, entry?.File ?? StorageJson.SafeFileName(name) + ".json");
    }

    // 다른 이름이 같은 파일명으로 줄어들면 (2), (3) … 을 붙인다
    private static string UniqueFile(List<IndexEntry> index, string file)
    {
        var used = new HashSet<string>(index.Select(e => e.File), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(file) && !File.Exists(Path.Combine(AppPaths.TemplatesDir, file))) return file;
        var stem = Path.GetFileNameWithoutExtension(file);
        for (var n = 2; n < 10000; n++)
        {
            var cand = $"{stem} ({n}).json";
            if (!used.Contains(cand) && !File.Exists(Path.Combine(AppPaths.TemplatesDir, cand))) return cand;
        }
        return $"{stem} ({DateTime.Now.Ticks}).json";
    }

    private static List<IndexEntry> ReadIndex()
    {
        if (!File.Exists(IndexFile)) return new List<IndexEntry>();
        try
        {
            var list = JsonSerializer.Deserialize<List<IndexEntry>>(File.ReadAllText(IndexFile), StorageJson.Indented);
            return list?.Where(e => !string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.File)).ToList() ?? new List<IndexEntry>();
        }
        catch (Exception ex)
        {
            AppLog.Warn("서식 목록을 읽을 수 없습니다: " + IndexFile, ex);
            return new List<IndexEntry>();
        }
    }

    private static void WriteIndex(List<IndexEntry> index)
        => StorageJson.WriteAtomic(IndexFile, JsonSerializer.Serialize(index, StorageJson.Indented));
}
