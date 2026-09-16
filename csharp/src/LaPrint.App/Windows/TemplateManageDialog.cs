// 서식 관리 — 저장된 서식 목록(객체 수·크기), 내보내기/삭제, 서식 파일 가져오기 (app.js manageTemplates).
using System.Windows;
using LaPrint.Core.Storage;

namespace LaPrint.App.Windows;

/// <summary>서식 관리 대화상자.</summary>
public sealed class TemplateManageDialog : Window
{
    private readonly TemplateStore _store;
    private readonly Func<Task> _onChanged;

    private TemplateManageDialog(Window owner, TemplateStore store, Func<Task> onChanged)
    {
        Owner = owner;
        _store = store;
        _onChanged = onChanged;
        Title = "서식 관리";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Build();
    }

    /// <summary>목록이 바뀔 때마다 onChanged (MainWindow.RefreshTemplateListAsync) 를 부른다.</summary>
    public static Task ShowAsync(Window owner, TemplateStore store, Func<Task> onChanged)
        => Task.CompletedTask; // TODO wave: new TemplateManageDialog(owner, store, onChanged).ShowDialog()

    /// <summary>표(이름 · "{n}개 객체 · {w}×{h}mm" · [내보내기][삭제]) + [서식 파일 가져오기…] + [닫기].</summary>
    private void Build() { /* TODO wave */ }

    private Task RefreshListAsync() => Task.CompletedTask; // TODO wave
}
