// 서식 관리 — 저장된 서식 목록(객체 수·크기), 내보내기/삭제, 서식 파일 가져오기 (app.js manageTemplates).
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaPrint.App.Services;
using LaPrint.Core.Model;
using LaPrint.Core.Storage;

namespace LaPrint.App.Windows;

/// <summary>서식 관리 대화상자.</summary>
public sealed class TemplateManageDialog : Window
{
    private readonly TemplateStore _store;
    private readonly Func<Task> _onChanged;
    private readonly StackPanel _list = new();

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

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
        Background = B("PaperBrush");
        Build();
    }

    /// <summary>목록이 바뀔 때마다 onChanged (MainWindow.RefreshTemplateListAsync) 를 부른다.</summary>
    public static Task ShowAsync(Window owner, TemplateStore store, Func<Task> onChanged)
    {
        new TemplateManageDialog(owner, store, onChanged).ShowDialog();
        return Task.CompletedTask;
    }

    /// <summary>표(이름 · "{n}개 객체 · {w}×{h}mm" · [내보내기][삭제]) + [서식 파일 가져오기…] + [닫기].</summary>
    private void Build()
    {
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        body.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var import = new Button { Content = "서식 파일 가져오기…", Style = S("SmallButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        import.Click += async (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "서식 파일 가져오기", Filter = "서식 JSON (*.json)|*.json|모든 파일 (*.*)|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var t = TemplateJson.Load(File.ReadAllText(dlg.FileName));
                var name = string.IsNullOrWhiteSpace(t.Name) ? Path.GetFileNameWithoutExtension(dlg.FileName) : t.Name.Trim();
                if (_store.List().Any(x => x.Name == name))
                {
                    var ok = await Dialogs.Confirm(this, $"\"{name}\" 서식이 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "덮어쓰기", "취소", danger: true);
                    if (!ok) return;
                }
                t.Name = name;
                _store.Save(name, t);
                await _onChanged();
                await RefreshListAsync();
                Dialogs.Toast(Owner, "서식을 가져왔습니다.", ToastLevel.Ok);
            }
            catch (Exception ex)
            {
                AppLog.Warn("서식 가져오기 실패: " + ex.Message);
                Dialogs.Toast(Owner, "가져오기 실패: " + ex.Message, ToastLevel.Err);
            }
        };
        body.Children.Add(import);

        var close = new Button { Content = "닫기", MinWidth = 84, IsDefault = true, IsCancel = true, Style = S("PrimaryButton") };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(close);
        body.Children.Add(buttons);
        Content = body;
        _ = RefreshListAsync();
    }

    private Task RefreshListAsync()
    {
        _list.Children.Clear();
        IReadOnlyList<(string Name, DateTime At)> list;
        try { list = _store.List(); }
        catch (Exception ex) { AppLog.Warn("서식 목록 읽기 실패: " + ex.Message); list = Array.Empty<(string, DateTime)>(); }
        if (list.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "저장된 서식이 없습니다. 레이아웃을 만든 뒤 상단 [저장]을 누르세요.",
                FontSize = 12, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap,
            });
            return Task.CompletedTask;
        }
        foreach (var (name, at) in list)
        {
            var t = _store.Load(name);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(new TextBlock
            {
                Text = t is null ? "읽을 수 없는 서식 파일" : $"{t.Objects.Count}개 객체 · {t.Label.W}×{t.Label.H}mm · 저장 {at:yyyy-MM-dd HH:mm}",
                FontSize = 11, Foreground = B(t is null ? "FailBrush" : "Ink3Brush"),
            });
            row.Children.Add(info);

            var exp = new Button { Content = "내보내기", Style = S("SmallGhostButton"), IsEnabled = t is not null };
            Grid.SetColumn(exp, 1);
            exp.Click += (_, _) =>
            {
                if (t is null) return;
                var dlg = new Microsoft.Win32.SaveFileDialog { Title = "서식 내보내기", FileName = name + ".json", DefaultExt = ".json", Filter = "서식 JSON (*.json)|*.json" };
                if (dlg.ShowDialog(this) != true) return;
                try
                {
                    t.Name = name;
                    File.WriteAllText(dlg.FileName, TemplateJson.Save(t), new System.Text.UTF8Encoding(false));
                    Dialogs.Toast(Owner, $"서식을 내보냈습니다: {Path.GetFileName(dlg.FileName)}", ToastLevel.Ok);
                }
                catch (Exception ex) { Dialogs.Toast(Owner, "내보내기 실패: " + ex.Message, ToastLevel.Err); }
            };
            row.Children.Add(exp);

            var del = new Button { Content = "삭제", Style = S("SmallDangerButton"), Margin = new Thickness(24, 2, 2, 2) };
            Grid.SetColumn(del, 2);
            del.Click += async (_, _) =>
            {
                var ok = await Dialogs.Confirm(this, $"서식 \"{name}\"을(를) 삭제할까요?", "서식 삭제", "삭제", "취소", danger: true);
                if (!ok) return;
                try
                {
                    _store.Delete(name);
                    await _onChanged();
                    await RefreshListAsync();
                    Dialogs.Toast(Owner, "삭제했습니다.", ToastLevel.Ok);
                }
                catch (Exception ex) { Dialogs.Toast(Owner, "삭제 실패: " + ex.Message, ToastLevel.Err); }
            };
            row.Children.Add(del);

            _list.Children.Add(new Border
            {
                BorderBrush = B("Line2Brush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4, 0, 4), Child = row,
            });
        }
        return Task.CompletedTask;
    }
}
