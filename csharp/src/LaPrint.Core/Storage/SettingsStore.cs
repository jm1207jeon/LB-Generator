// 설정 저장소 — System.Text.Json, 들여쓰기, tmp → Move 원자 저장. 깨진 파일은 기본값으로, 예전 구조는 프로필 구조로 옮긴다 (settings.js migrate).
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LaPrint.Core.Storage;

/// <summary>settings.json 읽기·쓰기. 저장 후 Changed 를 알린다.</summary>
public sealed class SettingsStore
{
    /// <summary>설정이 저장될 때마다.</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>파일이 없거나 깨졌으면 기본값. 빠진 키는 기본값으로 채운다 (deepMerge).</summary>
    public AppSettings Load()
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (node is not JsonObject obj) throw new JsonException("설정 파일이 아닙니다.");
            Migrate(obj);
            var s = obj.Deserialize<AppSettings>(StorageJson.Indented) ?? new AppSettings();
            return Normalize(s);
        }
        catch (Exception ex)
        {
            AppLog.Warn("설정 파일을 읽을 수 없어 기본값을 씁니다: " + path, ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        Normalize(s);
        StorageJson.WriteAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(s, StorageJson.Indented));
        RaiseChanged(s);
    }

    /// <summary>예전 구조(단일 열매칭 data.fieldMap/keyCol)를 프로필 구조로 옮긴다.</summary>
    public static void Migrate(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (Get(root, "data") is not JsonObject d) return;
        var fieldMap = Get(d, "fieldMap");
        var keyCol = Get(d, "keyCol");
        var hasFieldMap = fieldMap is JsonObject;
        var keyColText = keyCol is JsonValue kv && kv.TryGetValue<string>(out var ks) ? ks : null;
        var hasKeyCol = !string.IsNullOrEmpty(keyColText);
        if (Get(d, "profiles") is null && (hasFieldMap || hasKeyCol))
        {
            var key = hasKeyCol ? keyColText! : "H";
            d["profiles"] = new JsonObject
            {
                ["general"] = new JsonObject
                {
                    ["fieldMap"] = hasFieldMap ? fieldMap!.DeepClone() : new JsonObject(),
                    ["keyCol"] = key,
                },
                ["bsc"] = new JsonObject
                {
                    ["fieldMap"] = new JsonObject(),
                    ["keyCol"] = key,
                },
            };
        }
        Remove(d, "fieldMap");
        Remove(d, "keyCol");
    }

    // 하위 구조가 null 로 저장되어 있어도 기본값으로 채운다 — 프로필 두 개는 항상 있어야 한다
    private static AppSettings Normalize(AppSettings s)
    {
        s.Paths ??= new PathsSettings();
        s.Output ??= new OutputSettings();
        s.Imaging ??= new ImagingSettings();
        s.TextDefaults ??= new TextDefaults();
        s.BarcodeDefaults ??= new BarcodeDefaults();
        s.Printer ??= new PrinterSettings();
        s.Ui ??= new UiSettings();
        s.Validation ??= new Data.ValidationRules();
        s.Template ??= new TemplateSettings();
        s.Layout ??= new Export.LayoutOptions();
        s.Data ??= new DataSettings();
        s.Data.Profiles ??= new Dictionary<string, ProfileSettings>();
        foreach (var k in new[] { "general", "bsc" })
        {
            if (!s.Data.Profiles.TryGetValue(k, out var p) || p is null) s.Data.Profiles[k] = p = new ProfileSettings();
            p.FieldMap ??= new Dictionary<string, string>();
            if (string.IsNullOrEmpty(p.KeyCol)) p.KeyCol = "H";
        }
        if (string.IsNullOrEmpty(s.Data.Profile)) s.Data.Profile = "general";
        return s;
    }

    private static JsonNode? Get(JsonObject o, string key)
    {
        foreach (var kv in o)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    private static void Remove(JsonObject o, string key)
    {
        var names = o.Where(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
        foreach (var n in names) o.Remove(n);
    }

    private void RaiseChanged(AppSettings s) => Changed?.Invoke(s);
}
