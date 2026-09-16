// 설정 창 — 왼쪽 탐색(10 페이지) + 오른쪽 페이지. 설정은 바꾸는 즉시 AppSettings 에 반영되고 닫을 때 저장한다 (app.js openSettings).
using System.Windows;
using System.Windows.Controls;
using LaPrint.Core.Storage;

namespace LaPrint.App.Windows;

/// <summary>설정 창. 1000×820 CenterOwner, 작업 표시줄에 나타나지 않는다.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>탐색 페이지 (app.js PAGES 와 같은 키·문구).</summary>
    public static readonly IReadOnlyList<(string Key, string Title)> Pages = new (string, string)[]
    {
        ("paths", "폴더 경로"), ("mapping", "데이터 매칭"), ("layout", "라벨 · 용지"),
        ("output", "출력"), ("printer", "ZEBRA 프린터"), ("imaging", "이미지"),
        ("validation", "검증 규칙"), ("ui", "화면"), ("history", "출력 이력"), ("about", "정보"),
    };

    /// <summary>탐색 목록 항목.</summary>
    public sealed record NavItem(string Key, string Title);

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly HistoryStore? _history;
    private readonly Dictionary<string, FrameworkElement> _pages = new();

    public SettingsWindow(Window owner, AppSettings settings, SettingsStore store, HistoryStore? history, string? page)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _store = store;
        _history = history;
        nav.ItemsSource = Pages.Select(p => new NavItem(p.Key, p.Title)).ToList();
        nav.SelectedValue = Pages.Any(p => p.Key == page) ? page : "paths";
    }

    /// <summary>설정 창을 띄운다. 닫힐 때 저장했으면 true.</summary>
    public static Task<bool> ShowAsync(Window owner, AppSettings settings, SettingsStore store, HistoryStore? history = null, string? page = null)
    {
        var w = new SettingsWindow(owner, settings, store, history, page);
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>바뀐 값이 있었는지 (닫을 때 저장 여부).</summary>
    public bool Dirty { get; private set; }

    /// <summary>페이지 하나를 만든다 (app.js buildSettingsPages 의 해당 부분).</summary>
    private FrameworkElement BuildPage(string key)
        => throw new NotImplementedException("SettingsWindow.BuildPage — TODO wave");

    /// <summary>키로 페이지를 보인다 (한 번 만든 페이지는 캐시).</summary>
    private void SelectPage(string key) { /* TODO wave: _pages[key] ??= BuildPage(key); pageHost.Content = ... */ }

    /// <summary>설정 항목 한 줄 (라벨 + 컨트롤) — app.js settingRow.</summary>
    private static FrameworkElement SettingRow(string label, FrameworkElement ctl)
        => throw new NotImplementedException("SettingsWindow.SettingRow — TODO wave");

    private void MarkDirty() => Dirty = true;

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave: SelectPage(nav.SelectedValue as string) */ }

    private void OnCloseClick(object sender, RoutedEventArgs e) { /* TODO wave: if (Dirty) _store.Save(_settings); DialogResult = Dirty; Close(); */ }
}
