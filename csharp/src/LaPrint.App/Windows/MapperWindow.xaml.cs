// 데이터 매칭 편집기 — 라벨DB 를 표로 펼쳐 놓고 항목별 열을 지정(map 모드)하거나 열 하나를 고른다(pick 모드) (js/mapper.js).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LaPrint.Core.Data;

namespace LaPrint.App.Windows;

/// <summary>편집기가 보여줄 라벨DB 원본. Rows 는 표본(앞 N행)이어도 된다.</summary>
public sealed record MapperSource(IReadOnlyList<DbRow> Rows, DbRow? Header, int ColCount, string KeyCol, string Sheet, DbRow? SampleRow);

/// <summary>데이터 매칭 편집기 창. 별도 창(1100×760), 좌 항목 / 우 열 그리드.</summary>
public partial class MapperWindow : Window
{
    /// <summary>"map" | "pick".</summary>
    private readonly string _mode;
    private readonly MapperSource? _src;
    private readonly FieldMap? _map;
    /// <summary>편집 중 초안 매핑 (적용 전).</summary>
    private Dictionary<string, string> Draft { get; set; } = new();
    private string DraftKey { get; set; } = "H";
    private string? SelectedField { get; set; }
    /// <summary>pick 모드 결과.</summary>
    private (string Col, string Value)? Picked { get; set; }

    private MapperWindow(Window owner, string mode, MapperSource? src, FieldMap? map, string? hint)
    {
        InitializeComponent();
        Owner = owner;
        _mode = mode;
        _src = src;
        _map = map;
        if (mode == "pick")
        {
            Title = "DB 열 고르기";
            fieldsPane.Visibility = Visibility.Collapsed;
            btnOk.Content = "이 열로";
            btnReset.Visibility = Visibility.Collapsed;
            hintText.Text = hint ?? "표에서 열을 클릭하면 그 열이 지정됩니다.";
        }
        else if (src is null || src.Rows.Count == 0)
        {
            hintText.Text = "라벨DB를 먼저 불러오면 실제 값을 보면서 지정할 수 있습니다.";
        }
    }

    /// <summary>매칭 편집기. 적용했으면 true (map 이 바뀌어 있다).</summary>
    public static Task<bool> OpenEditorAsync(Window owner, MapperSource? src, FieldMap map, DbRow? sampleRow = null)
    {
        var w = new MapperWindow(owner, "map", src is null ? null : src with { SampleRow = sampleRow ?? src.SampleRow ?? src.Rows.FirstOrDefault() }, map, null);
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>열 하나 고르기. 취소면 null. Value 는 표본 행의 그 열 값.</summary>
    public static Task<(string Col, string Value)?> PickColumnAsync(Window owner, MapperSource? src, string? hint = null, DbRow? sampleRow = null)
    {
        var w = new MapperWindow(owner, "pick", src is null ? null : src with { SampleRow = sampleRow ?? src.SampleRow ?? src.Rows.FirstOrDefault() }, null, hint);
        return Task.FromResult(w.ShowDialog() == true ? w.Picked : null);
    }

    /// <summary>오른쪽 표를 만든다 (열 문자 머리글, 앞 N행) — mapper.js buildGrid.</summary>
    private void BuildGrid() { /* TODO wave */ }

    /// <summary>지정된 열·선택 열을 색으로 표시 — mapper.js paintGrid.</summary>
    private void PaintGrid() { /* TODO wave */ }

    /// <summary>왼쪽 항목 목록을 그룹별로 만든다 — mapper.js buildFields.</summary>
    private void BuildFields() { /* TODO wave */ }

    /// <summary>선택한 항목(또는 pick 결과)에 열을 지정 — mapper.js assign.</summary>
    private void Assign(string colName) { /* TODO wave */ }

    private void SelectField(string key) { /* TODO wave */ }

    /// <summary>항목 행의 표본 값을 갱신 — mapper.js refreshFields.</summary>
    private void RefreshFields() { /* TODO wave */ }

    /// <summary>초안을 FieldMap 에 적용 — mapper.js persist.</summary>
    private void Persist() { /* TODO wave */ }

    private void OnGridClick(object sender, MouseButtonEventArgs e) { /* TODO wave: 클릭한 셀의 열 → Assign */ }
    private void OnKeyColChanged(object sender, TextChangedEventArgs e) { /* TODO wave */ }
    private void OnResetClick(object sender, RoutedEventArgs e) { /* TODO wave: 프로필 기본값으로 */ }
    private void OnCancelClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void OnOkClick(object sender, RoutedEventArgs e) { /* TODO wave: map → Persist(); DialogResult = true; Close(); */ }
}
