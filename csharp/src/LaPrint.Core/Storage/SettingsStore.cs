// 설정 저장소 — System.Text.Json, 들여쓰기, tmp → Move 원자 저장.
namespace LaPrint.Core.Storage;

/// <summary>settings.json 읽기·쓰기. 저장 후 Changed 를 알린다.</summary>
public sealed class SettingsStore
{
    /// <summary>설정이 저장될 때마다.</summary>
    public event Action<AppSettings>? Changed;

    public AppSettings Load()
        => throw new NotImplementedException("SettingsStore.Load — 아직 구현되지 않았습니다");

    public void Save(AppSettings s)
        => throw new NotImplementedException("SettingsStore.Save — 아직 구현되지 않았습니다");

    private void RaiseChanged(AppSettings s) => Changed?.Invoke(s);
}
