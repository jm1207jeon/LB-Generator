// 공용 대화상자 — 확인(기본 버튼은 언제나 아니오/취소, Esc = 취소) · 입력 · 토스트 (ui.js confirm/prompt/toast).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LaPrint.App.Services;

/// <summary>토스트 수준 (ui.js toast: '' | 'ok' | 'warn' | 'err').</summary>
public enum ToastLevel { Info, Ok, Warn, Err }

/// <summary>공용 대화상자. Confirm/Prompt 는 ShowDialog 로 동기 완료된 Task 를 돌려준다 (Closing 에서도 안전).</summary>
public static class Dialogs
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

    /// <summary>확인 대화상자. 기본(Enter)·Esc 는 취소(아니오). danger 면 확인 버튼이 파괴적 색.</summary>
    public static Task<bool> Confirm(Window? owner, string msg, string title = "확인", string okLabel = "예",
                                     string cancelLabel = "아니오", bool danger = false, string detail = "")
    {
        var w = MakeWindow(owner, title, 480);
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        body.Children.Add(new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = B("InkBrush") });
        if (!string.IsNullOrEmpty(detail))
            body.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = B("Ink3Brush"), Margin = new Thickness(0, 8, 0, 0) });

        var ok = new Button { Content = okLabel, MinWidth = 84, Style = S(danger ? "DangerButton" : "PrimaryButton") };
        var cancel = new Button { Content = cancelLabel, MinWidth = 84, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        w.Content = body;
        w.Loaded += (_, _) => cancel.Focus();
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>한 줄 입력 대화상자. 취소면 null.</summary>
    public static Task<string?> Prompt(Window? owner, string msg, string title = "입력", string value = "",
                                       string placeholder = "", string okLabel = "확인")
    {
        var w = MakeWindow(owner, title, 460);
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        body.Children.Add(new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = B("InkBrush") });
        var input = new TextBox { Text = value, FontSize = 15, Margin = new Thickness(0, 8, 0, 0), ToolTip = string.IsNullOrEmpty(placeholder) ? null : placeholder };
        body.Children.Add(input);

        string? result = null;
        var ok = new Button { Content = okLabel, MinWidth = 84, Style = S("PrimaryButton") };
        var cancel = new Button { Content = "취소", MinWidth = 84, IsCancel = true };
        ok.Click += (_, _) => { result = input.Text; w.DialogResult = true; w.Close(); };
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { result = input.Text; w.DialogResult = true; w.Close(); } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        w.Content = body;
        w.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return Task.FromResult(w.ShowDialog() == true ? result : null);
    }

    /// <summary>내용만 보여 주는 대화상자 (ui.js modal 의 [닫기] 하나짜리). 닫기가 기본·Esc.</summary>
    public static void Info(Window? owner, string title, FrameworkElement body, double width = 520, string closeLabel = "닫기")
    {
        var w = MakeWindow(owner, title, width);
        w.MaxHeight = 720;
        var root = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
        var close = new Button { Content = closeLabel, MinWidth = 84, IsDefault = true, IsCancel = true, Style = S("PrimaryButton") };
        close.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 600 });
        w.Content = root;
        w.Loaded += (_, _) => close.Focus();
        w.ShowDialog();
    }

    /// <summary>탐색기로 폴더를 연다. 실패는 토스트로 알린다.</summary>
    public static void OpenFolder(Window? owner, string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast(owner, "폴더를 열지 못했습니다: " + ex.Message, ToastLevel.Err);
        }
    }

    /// <summary>오른쪽 아래 잠깐 뜨는 알림. owner 의 "toastHost" 패널에 붙인다 (없으면 상태 없이 무시).</summary>
    public static void Toast(Window? owner, string msg, ToastLevel level = ToastLevel.Info, int? ms = null)
    {
        if (owner?.FindName("toastHost") is not Panel host) return;
        var (bg, fg) = level switch
        {
            ToastLevel.Ok => (B("PassBrush"), Brushes.White),
            ToastLevel.Warn => (B("WarnFillBrush"), B("WarnBrush")),
            ToastLevel.Err => (B("DangerBrush"), Brushes.White),
            _ => (B("InkBrush"), Brushes.White),
        };
        var el = new Border
        {
            Background = bg, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Right,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.35 },
            Child = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap, Foreground = fg, FontSize = 13 },
        };
        host.Children.Add(el);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms ?? (level == ToastLevel.Err ? 5200 : 2800)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => host.Children.Remove(el);
            el.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        timer.Start();
    }

    private static Window MakeWindow(Window? owner, string title, double width)
    {
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Background = B("PaperBrush"),
        };
        if (owner is not null && owner.IsLoaded) w.Owner = owner;
        return w;
    }
}
