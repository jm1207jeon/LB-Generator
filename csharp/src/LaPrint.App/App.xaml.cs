// 앱 진입 — 가족 앱(UDInspect·LaVis)과 같이 코드비하인드 구성. 단일 인스턴스 Mutex, 처리되지 않은 예외는 로그+알림 뒤 계속 실행.
using System.Windows;
using LaPrint.App.Services;

namespace LaPrint.App;

/// <summary>LaPrint 애플리케이션.</summary>
public partial class App : Application
{
    private const string MutexName = @"Local\LaPrint_SingleInstance";
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("LaPrint가 이미 실행 중입니다.", "LaPrint", MessageBoxButton.OK, MessageBoxImage.Information);
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        var ver = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        AppLog.Info($"LaPrint v{ver} 시작 — {Environment.OSVersion} · .NET {Environment.Version}");

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("처리되지 않은 예외", args.Exception);
            MessageBox.Show(
                $"예기치 않은 오류가 발생했습니다:\n{args.Exception.Message}\n\n자세한 내용은 로그를 확인하세요:\n{AppLog.FilePath}",
                "LaPrint 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("관찰되지 않은 작업 예외", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("도메인 예외", args.ExceptionObject as Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("LaPrint 종료");
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
