// 앱 진입 — 가족 앱(UDInspect·LaVis)과 같이 코드비하인드 구성. 처리되지 않은 예외는 알린 뒤 계속 실행한다.
using System.Windows;

namespace LaPrint.App;

/// <summary>LaPrint 애플리케이션.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"예기치 않은 오류가 발생했습니다:\n{args.Exception.Message}",
                            "LaPrint 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
