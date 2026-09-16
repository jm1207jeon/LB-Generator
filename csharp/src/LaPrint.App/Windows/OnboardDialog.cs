// 처음 설정 안내 — 라벨DB 지정 · 이미지 폴더 · 저장 폴더 · 샘플 데이터 단계 (app.js showOnboard).
using System.Windows;

namespace LaPrint.App.Windows;

/// <summary>처음 시작 안내. 단계마다 완료 표시와 바로가기 동작.</summary>
public sealed class OnboardDialog : Window
{
    private readonly bool _dbDone;
    private readonly bool _imgDone;
    private readonly bool _outDone;

    private OnboardDialog(Window owner, bool dbDone, bool imgDone, bool outDone)
    {
        Owner = owner;
        _dbDone = dbDone; _imgDone = imgDone; _outDone = outDone;
        Title = "처음 설정";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Build();
    }

    /// <summary>사용자가 고른 동작 키: "db" | "img" | "out" | "sample" | null(닫음).</summary>
    public static Task<string?> ShowAsync(Window owner, bool dbDone, bool imgDone, bool outDone)
        => throw new NotImplementedException("OnboardDialog.ShowAsync — TODO wave");

    /// <summary>단계 목록(제목·설명·완료 ✓·바로가기 버튼) + [나중에].</summary>
    private void Build() { /* TODO wave */ }
}
