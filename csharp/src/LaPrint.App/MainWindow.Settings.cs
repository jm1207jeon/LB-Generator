// 설정 창 · 데이터 매칭 편집기 · 처음 설정 안내 · 도움말 (app.js openSettings/openDataMapper/maybeOnboard/showHelp/checkReconnect).
// ★ TODO wave — 이 파일의 본문을 채우는 에이전트가 있다. 시그니처는 바꾸지 않는다.
using System.Windows;
using LaPrint.App.Windows;
using LaPrint.Core.Storage;

namespace LaPrint.App;

public partial class MainWindow
{
    /// <summary>SettingsWindow.ShowAsync(this, Settings, SettingsStore, History, page) → ApplyUiSettings, ApplyProfileToMap, Images 재생성, UpdateChips, Refresh (app.js openSettings).</summary>
    private Task OpenSettingsAsync(string? page = null) => Task.CompletedTask; // TODO wave

    /// <summary>MapperWindow.OpenEditorAsync(this, MapperSource(Db…), Map, Row) → 적용이면 Settings.Data.Profiles[ProfileKey] 갱신·저장, 색인 재구축, Refresh (app.js openDataMapper).</summary>
    private Task OpenDataMapperAsync() => Task.CompletedTask; // TODO wave

    /// <summary>Settings.Ui.OnboardingDone 이 아니면 OnboardDialog → 동작 키(db/img/out/sample)에 따라 설정 열기 또는 LoadSampleDataAsync (app.js maybeOnboard/showOnboard).</summary>
    private Task MaybeOnboardAsync() => Task.CompletedTask; // TODO wave

    /// <summary>브라우저판의 폴더 권한 재연결 확인. C# 에서는 폴더가 권한을 잃지 않으므로 할 일이 없다 — 호출 순서를 지키기 위한 no-op.</summary>
    private Task CheckReconnectAsync() => Task.CompletedTask;

    /// <summary>F1 — 단축키 도움말 (app.js showHelp).</summary>
    private void ShowHelp() { /* TODO wave */ }

    /// <summary>SettingsStore.Changed — 다른 창에서 저장했을 때 Settings 교체 + ApplyUiSettings + UpdateChips.</summary>
    private void OnSettingsChanged(AppSettings s) { /* TODO wave */ }

    private void OnSettingsClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(() => OpenSettingsAsync(null)) */ }
    private void OnHelpClick(object sender, RoutedEventArgs e) { /* TODO wave: ShowHelp() */ }
    /// <summary>chipDb/chipImg/chipOut — Tag 페이지("paths")로 설정 열기.</summary>
    private void OnChipClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(() => OpenSettingsAsync("paths")) */ }
}
