// 저장소 — 설정 왕복·깨진 파일·예전 구조 이전, 서식 목록, 세션 왕복, 이력 순서, 라벨DB 캐시, 앱 로그 (settings.js · store.js 와 같은 동작).
using System.Text.Json;
using LaPrint.Core.Batch;
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using LaPrint.Core.Storage;
using Xunit;

namespace LaPrint.Core.Tests.Storage;

// AppPaths 는 전역이므로 저장소 테스트는 병렬로 돌리지 않는다
[CollectionDefinition("Storage", DisableParallelization = true)]
public class StorageCollection { }

[Collection("Storage")]
public class StorageTests
{
    private readonly string _root;

    public StorageTests()
    {
        _root = Path.Combine(AppContext.BaseDirectory, "_storage_tmp", Guid.NewGuid().ToString("N"));
        AppPaths.Override(_root);
    }

    private static string Json(object o) => JsonSerializer.Serialize(o, StorageJson.Indented);

    /* ---------------- AppPaths ---------------- */

    [Fact]
    public void AppPaths_Override_MovesAllFiles()
    {
        Assert.Equal(Path.GetFullPath(_root), AppPaths.Root);
        Assert.Equal(Path.Combine(AppPaths.Root, "settings.json"), AppPaths.SettingsFile);
        Assert.Equal(Path.Combine(AppPaths.Root, "templates"), AppPaths.TemplatesDir);
        Assert.Equal(Path.Combine(AppPaths.Root, "session.json"), AppPaths.SessionFile);
        Assert.Equal(Path.Combine(AppPaths.Root, "history.jsonl"), AppPaths.HistoryFile);
        Assert.Equal(Path.Combine(AppPaths.Root, "cache"), AppPaths.CacheDir);
        Assert.Equal(Path.Combine(AppPaths.Root, "app.log"), AppPaths.LogFile);
    }

    /* ---------------- SettingsStore ---------------- */

    [Fact]
    public void Settings_MissingFile_IsDefaults()
    {
        var s = new SettingsStore().Load();
        Assert.Equal(Json(new AppSettings()), Json(s));
    }

    [Fact]
    public void Settings_RoundTrip_EqualsDefaults_And_CamelCase()
    {
        var store = new SettingsStore();
        AppSettings? notified = null;
        store.Changed += x => notified = x;
        var d = new AppSettings();
        store.Save(d);
        Assert.Same(d, notified);
        Assert.True(File.Exists(AppPaths.SettingsFile));
        Assert.False(File.Exists(AppPaths.SettingsFile + ".tmp"));

        var text = File.ReadAllText(AppPaths.SettingsFile);
        Assert.Contains("\"output\"", text);
        Assert.Contains("\"pattern\": \"{ITEM}_{LOT}_{DATE}\"", text);
        Assert.Contains("\"profiles\"", text);
        Assert.Contains("\"general\"", text);
        Assert.Contains("\"bsc\"", text);
        Assert.Contains("\"keyCol\": \"H\"", text);
        Assert.DoesNotContain("\"Output\"", text);

        var back = store.Load();
        Assert.Equal(Json(new AppSettings()), Json(back));
    }

    [Fact]
    public void Settings_ChangedValues_Survive_And_MissingKeysFilled()
    {
        var store = new SettingsStore();
        var s = new AppSettings();
        s.Output.Dpi = 600;
        s.Output.Mode = "merged";
        s.Paths.DbDir = @"\\서버\라벨DB";
        s.Paths.DbLastModified = new DateTime(2024, 5, 6, 7, 8, 9);
        s.Printer.Darkness = 12;
        s.Data.Profile = "bsc";
        s.Data.Profiles["bsc"].FieldMap["GTIN"] = "AB";
        s.Data.Profiles["bsc"].KeyCol = "C";
        s.Validation.RequireSn = true;
        s.Layout.Paper = "A4";
        store.Save(s);
        var back = store.Load();
        Assert.Equal(600, back.Output.Dpi);
        Assert.Equal("merged", back.Output.Mode);
        Assert.Equal(@"\\서버\라벨DB", back.Paths.DbDir);
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9), back.Paths.DbLastModified);
        Assert.Equal(12, back.Printer.Darkness);
        Assert.Null(back.Printer.Speed);
        Assert.Equal("bsc", back.Data.Profile);
        Assert.Equal("AB", back.Data.Profiles["bsc"].FieldMap["GTIN"]);
        Assert.Equal("C", back.Data.Profiles["bsc"].KeyCol);
        Assert.Equal("H", back.Data.Profiles["general"].KeyCol);
        Assert.True(back.Validation.RequireSn);
        Assert.Equal("A4", back.Layout.Paper);

        // 일부 키만 있는 파일 — 나머지는 기본값 (deepMerge)
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.SettingsFile, "{ \"output\": { \"dpi\": 150 }, \"ui\": { \"theme\": \"dark\" }, \"data\": { \"profiles\": { \"general\": { \"keyCol\": \"B\" } } } }");
        var partial = store.Load();
        Assert.Equal(150, partial.Output.Dpi);
        Assert.Equal("separate", partial.Output.Mode);
        Assert.Equal("dark", partial.Ui.Theme);
        Assert.True(partial.Ui.ShowRulers);
        Assert.Equal("B", partial.Data.Profiles["general"].KeyCol);
        Assert.True(partial.Data.Profiles.ContainsKey("bsc"));
        Assert.Equal("H", partial.Data.Profiles["bsc"].KeyCol);
        Assert.Equal(203, partial.Printer.Dpi);
    }

    [Fact]
    public void Settings_CorruptedFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.SettingsFile, "{ \"output\": { \"dpi\": ");
        var s = new SettingsStore().Load();
        Assert.Equal(Json(new AppSettings()), Json(s));

        File.WriteAllText(AppPaths.SettingsFile, "[1, 2, 3]");
        Assert.Equal(Json(new AppSettings()), Json(new SettingsStore().Load()));

        File.WriteAllText(AppPaths.SettingsFile, "{ \"output\": null, \"data\": null }");
        var nulls = new SettingsStore().Load();
        Assert.NotNull(nulls.Output);
        Assert.Equal(300, nulls.Output.Dpi);
        Assert.True(nulls.Data.Profiles.ContainsKey("general"));
    }

    [Fact]
    public void Settings_LegacyFieldMap_MigratesIntoGeneralProfile()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.SettingsFile,
            "{ \"data\": { \"profile\": \"general\", \"fieldMap\": { \"GTIN\": \"Z\", \"LOT_PREFIX\": \"\" }, \"keyCol\": \"J\" }, \"output\": { \"dpi\": 200 } }");
        var s = new SettingsStore().Load();
        Assert.Equal("Z", s.Data.Profiles["general"].FieldMap["GTIN"]);
        Assert.Equal("", s.Data.Profiles["general"].FieldMap["LOT_PREFIX"]);
        Assert.Equal("J", s.Data.Profiles["general"].KeyCol);
        Assert.Empty(s.Data.Profiles["bsc"].FieldMap);
        Assert.Equal("J", s.Data.Profiles["bsc"].KeyCol);
        Assert.Equal(200, s.Output.Dpi);

        // keyCol 만 있어도 옮긴다
        File.WriteAllText(AppPaths.SettingsFile, "{ \"data\": { \"keyCol\": \"K\" } }");
        var k = new SettingsStore().Load();
        Assert.Equal("K", k.Data.Profiles["general"].KeyCol);
        Assert.Empty(k.Data.Profiles["general"].FieldMap);

        // 이미 프로필 구조면 예전 키는 버린다
        File.WriteAllText(AppPaths.SettingsFile,
            "{ \"data\": { \"fieldMap\": { \"GTIN\": \"Q\" }, \"keyCol\": \"Q\", \"profiles\": { \"general\": { \"fieldMap\": {}, \"keyCol\": \"H\" }, \"bsc\": { \"fieldMap\": {}, \"keyCol\": \"H\" } } } }");
        var p = new SettingsStore().Load();
        Assert.Equal("H", p.Data.Profiles["general"].KeyCol);
        Assert.Empty(p.Data.Profiles["general"].FieldMap);
    }

    /* ---------------- TemplateStore ---------------- */

    [Fact]
    public void Templates_Save_List_Load_Delete_KoreanName()
    {
        var store = new TemplateStore();
        Assert.Empty(store.List());
        Assert.Null(store.Load("없음"));

        var t = new LabelTemplate
        {
            Name = "기본 라벨",
            Label = new LabelSize { W = 173.8, H = 75, Bg = "" },
            Objects =
            {
                new TextObject { Id = "o1", X = 1, Y = 2, W = 30, H = 5, Text = "{ITEM}", Font = "Arial", SizePt = 9 },
                new BarcodeObject { Id = "o2", X = 5, Y = 10, W = 20, H = 20, Symbology = "gs1datamatrix" },
            },
        };
        var before = DateTime.Now.AddSeconds(-1);
        store.Save("스텐트 라벨/일반: 시험", t);
        store.Save("둘째", t);
        var list = store.List();
        Assert.Equal(new[] { "스텐트 라벨/일반: 시험", "둘째" }, list.Select(x => x.Name).ToArray());
        Assert.All(list, x => Assert.True(x.At >= before));
        Assert.True(File.Exists(Path.Combine(AppPaths.TemplatesDir, "index.json")));
        Assert.Contains(Directory.GetFiles(AppPaths.TemplatesDir, "*.json"), f => Path.GetFileName(f).StartsWith("스텐트 라벨_일반", StringComparison.Ordinal));

        var back = store.Load("스텐트 라벨/일반: 시험");
        Assert.NotNull(back);
        Assert.Equal(173.8, back!.Label.W);
        Assert.Equal(2, back.Objects.Count);
        var txt = Assert.IsType<TextObject>(back.Objects[0]);
        Assert.Equal("{ITEM}", txt.Text);
        Assert.Equal(9, txt.SizePt);
        Assert.IsType<BarcodeObject>(back.Objects[1]);

        // 같은 이름 덮어쓰기 — 목록은 그대로, 내용만 바뀐다
        t.Objects.RemoveAt(1);
        store.Save("둘째", t);
        Assert.Equal(2, store.List().Count);
        Assert.Single(store.Load("둘째")!.Objects);

        store.Delete("스텐트 라벨/일반: 시험");
        Assert.Equal(new[] { "둘째" }, store.List().Select(x => x.Name).ToArray());
        Assert.Null(store.Load("스텐트 라벨/일반: 시험"));
        store.Delete("없음");   // 없는 이름은 조용히
        Assert.Single(store.List());
    }

    /* ---------------- SessionStore ---------------- */

    [Fact]
    public void Session_RoundTrip()
    {
        var store = new SessionStore();
        Assert.Null(store.Load());

        var s = new SessionState
        {
            Inputs = new JobInputs("PN-1", "L1", "S1", "2024-01-15", 24, false, "2026-01-14"),
            Locked = false,
            Queue =
            {
                new QueueRow { Item = "PN-1", Lot = "L1", Sn = "001", Mfg = "2024-01-15", Copies = 2, Status = "done", FileName = "a.pdf",
                               Issues = { new Issue("warn", "OVERFLOW", "넘침", null, "o1") },
                               Fields = new Fields { ["ITEM"] = "PN-1" }, Row = new DbRow { ["A"] = "x" } },
                new QueueRow { Item = "PN-2", Lot = "L2", Status = "running", Error = "" },
            },
        };
        store.Save(s);
        Assert.NotEqual(default, s.SavedAt);
        var text = File.ReadAllText(AppPaths.SessionFile);
        Assert.Contains("\"inputs\"", text);
        Assert.Contains("\"queue\"", text);
        Assert.Contains("\"savedAt\"", text);
        Assert.DoesNotContain("\"fields\"", text);   // 런타임 값은 저장하지 않는다
        Assert.DoesNotContain("\"row\"", text);

        var back = store.Load();
        Assert.NotNull(back);
        Assert.Equal(s.Inputs, back!.Inputs);
        Assert.False(back.Locked);
        Assert.Equal(s.SavedAt, back.SavedAt);
        Assert.Equal(2, back.Queue.Count);
        Assert.Equal(s.Queue[0].Id, back.Queue[0].Id);
        Assert.Equal("001", back.Queue[0].Sn);
        Assert.Equal(2, back.Queue[0].Copies);
        Assert.Equal("done", back.Queue[0].Status);
        Assert.Equal("a.pdf", back.Queue[0].FileName);
        Assert.Single(back.Queue[0].Issues);
        Assert.Equal(new Issue("warn", "OVERFLOW", "넘침", null, "o1"), back.Queue[0].Issues[0]);
        Assert.Null(back.Queue[0].Fields);
        Assert.Equal("pending", back.Queue[1].Status);   // 처리중이던 행은 대기로

        File.WriteAllText(AppPaths.SessionFile, "{ broken");
        Assert.Null(store.Load());
    }

    /* ---------------- HistoryStore ---------------- */

    [Fact]
    public void History_Append_List_NewestFirst()
    {
        var store = new HistoryStore();
        Assert.Empty(store.List(10));
        var t0 = new DateTime(2024, 3, 1, 9, 0, 0);
        for (var i = 0; i < 5; i++)
            store.Append(new HistoryEntry { At = t0.AddMinutes(i), Item = "PN-" + i, Lot = "L" + i, Sn = "S" + i, FileName = $"f{i}.pdf", Ok = i != 3, Error = i == 3 ? "실패 \"따옴표\"" : null, Profile = i % 2 == 0 ? "general" : "bsc", Udi = "(01)08806\n" });

        var lines = File.ReadAllLines(AppPaths.HistoryFile);
        Assert.Equal(5, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("{", l));
        Assert.Contains("\"profile\":\"bsc\"", lines[1]);

        var list = store.List(3);
        Assert.Equal(new[] { "PN-4", "PN-3", "PN-2" }, list.Select(e => e.Item).ToArray());
        Assert.False(list[1].Ok);
        Assert.Equal("실패 \"따옴표\"", list[1].Error);
        Assert.Equal(t0.AddMinutes(4), list[0].At);
        Assert.Equal("(01)08806\n", list[0].Udi);
        Assert.Equal(5, store.List(100).Count);
        Assert.Empty(store.List(0));

        // 깨진 줄은 건너뛴다
        File.AppendAllText(AppPaths.HistoryFile, "{ not json\n");
        Assert.Equal(5, store.List(100).Count);
    }

    /* ---------------- DbCache ---------------- */

    [Fact]
    public void DbCache_Put_Get_Invalidate()
    {
        var cache = new DbCache();
        var stamp = new DateTime(2024, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        Assert.Null(cache.Get("general", stamp));

        var rows = new List<DbRow>();
        for (var i = 0; i < 300; i++)
            rows.Add(new DbRow { ["A"] = "a" + i, ["H"] = "0880600" + i, ["BC"] = i % 7 == 0 ? "E.JPG" : "" });
        var header = new DbRow { ["A"] = "No", ["H"] = "Product Number" };
        var db = new LabelDb(rows, "라벨DB", new[] { ("라벨DB", 300), ("기타", 2) }, header, 55);
        cache.Put("general", db, stamp);
        Assert.True(File.Exists(Path.Combine(AppPaths.CacheDir, "general.db.json.gz")));

        var back = cache.Get("general", stamp);
        Assert.NotNull(back);
        Assert.Equal(300, back!.Rows.Count);
        Assert.Equal("라벨DB", back.Sheet);
        Assert.Equal(55, back.ColCount);
        Assert.Equal(("라벨DB", 300), back.Sheets[0]);
        Assert.Equal(("기타", 2), back.Sheets[1]);
        Assert.Equal("Product Number", back.Header!["H"]);
        Assert.Equal("08806007", back.Rows[7]["H"]);
        Assert.Equal("E.JPG", back.Rows[7]["BC"]);
        Assert.IsType<DbRow>(back.Rows[0]);

        Assert.Null(cache.Get("general", stamp.AddSeconds(1)));   // 수정 시각이 다르면 무효
        Assert.Null(cache.Get("bsc", stamp));                     // 프로필별 캐시

        cache.Invalidate("general");
        Assert.Null(cache.Get("general", stamp));
        cache.Invalidate("general");   // 없는 캐시도 조용히
    }

    /* ---------------- AppLog ---------------- */

    [Fact]
    public void AppLog_WritesLine_And_Rotates()
    {
        AppLog.Info("시작");
        AppLog.Warn("경고 메시지", new InvalidOperationException("이유"));
        AppLog.Error("오류 메시지");
        Assert.True(File.Exists(AppPaths.LogFile));
        var lines = File.ReadAllLines(AppPaths.LogFile);
        Assert.Equal(3, lines.Length);
        Assert.Contains("[INFO] 시작", lines[0]);
        Assert.Contains("[WARN] 경고 메시지 | InvalidOperationException: 이유", lines[1]);
        Assert.Contains("[ERROR] 오류 메시지", lines[2]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[", lines[0]);

        // 1 MB 를 넘으면 app.prev.log 로 돌린다
        File.WriteAllBytes(AppPaths.LogFile, new byte[AppLog.RotateBytes + 1]);
        AppLog.Info("회전 후");
        Assert.True(File.Exists(AppPaths.PrevLogFile));
        Assert.Equal(AppLog.RotateBytes + 1, new FileInfo(AppPaths.PrevLogFile).Length);
        Assert.Single(File.ReadAllLines(AppPaths.LogFile));
    }
}
