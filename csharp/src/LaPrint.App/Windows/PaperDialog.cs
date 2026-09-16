// 용지 배치 대화상자 — 용지·방향·여백·간격·정렬·재단선·외곽선·반복, 배치 미리보기 (app.js openPaperDialog).
using System.Windows;
using LaPrint.Core.Export;
using LaPrint.Core.Model;

namespace LaPrint.App.Windows;

/// <summary>용지 배치 설정. 적용하면 opt 가 바뀐 채 true.</summary>
public sealed class PaperDialog : Window
{
    private readonly LabelSize _label;
    private readonly LayoutOptions _opt;

    private PaperDialog(Window owner, LabelSize label, LayoutOptions opt)
    {
        Owner = owner;
        _label = label;
        _opt = opt;
        Title = "용지 배치";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Build();
    }

    /// <summary>대화상자를 띄운다. 적용이면 true (opt 갱신됨), 취소면 false.</summary>
    public static Task<bool> ShowAsync(Window owner, LabelSize label, LayoutOptions opt)
        => throw new NotImplementedException("PaperDialog.ShowAsync — TODO wave");

    /// <summary>폼(용지 콤보·사용자 지정 W/H·방향·여백·간격·정렬·재단선·외곽선·반복) + 미리보기(Paper.Plan/Describe).</summary>
    private void Build() { /* TODO wave */ }

    /// <summary>입력값을 _opt 에 적용하고 미리보기 문구를 갱신.</summary>
    private void Refresh() { /* TODO wave */ }
}
