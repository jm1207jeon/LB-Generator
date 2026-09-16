// 원판에서 떼어내기 — A3 세트 서식에서 라벨 규격 하나를 골라 독립 서식으로 만든다 (app.js openExtractDialog).
using System.Windows;
using LaPrint.Core.Export;
using LaPrint.Core.Model;

namespace LaPrint.App.Windows;

/// <summary>사용자가 고른 떼어내기 조건.</summary>
public sealed record ExtractChoice(LabelPreset Preset, bool IncludePartial, bool KeepBackground);

/// <summary>원판에서 떼어내기 대화상자. 프리셋별 (들어가는 객체 N · 걸치는 객체) 미리보기 표.</summary>
public sealed class ExtractDialog : Window
{
    private readonly LabelTemplate _sheet;

    private ExtractDialog(Window owner, LabelTemplate sheet)
    {
        Owner = owner;
        _sheet = sheet;
        Title = "원판에서 라벨 떼어내기";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Build();
    }

    /// <summary>선택 결과. 취소면 null. (호출자가 SheetExtractor.ExtractPreset 로 실제 떼어낸다)</summary>
    public static Task<ExtractChoice?> ShowAsync(Window owner, LabelTemplate sheet)
        => throw new NotImplementedException("ExtractDialog.ShowAsync — TODO wave");

    /// <summary>SheetExtractor.Preview(sheet) 표 + 옵션(걸치는 객체 포함 · 배경 유지) + [떼어내기]/[취소].</summary>
    private void Build() { /* TODO wave */ }
}
