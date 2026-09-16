// 글꼴 공급 — 요청 글꼴이 없으면 계량이 비슷한 대체 글꼴(Windows 맑은 고딕/Arial · Linux Liberation Sans).
using SkiaSharp;

namespace LaPrint.Core.Typography;

/// <summary>글꼴 선택과 폴백.</summary>
public static class FontProvider
{
    public static SKTypeface Get(string family, bool bold, bool italic)
        => throw new NotImplementedException("FontProvider.Get — 아직 구현되지 않았습니다");

    public static bool IsAvailable(string family)
        => throw new NotImplementedException("FontProvider.IsAvailable — 아직 구현되지 않았습니다");

    /// <summary>글꼴 선택 목록 (text.js FONTS).</summary>
    public static IReadOnlyList<(string Id, string Name)> Choices { get; } = new (string, string)[]
    {
        ("Arial", "Arial"),
        ("Helvetica", "Helvetica"),
        ("Times New Roman", "Times New Roman"),
        ("Courier New", "Courier New"),
        ("Tahoma", "Tahoma"),
        ("Verdana", "Verdana"),
        ("Calibri", "Calibri"),
        ("Segoe UI", "Segoe UI"),
        ("Malgun Gothic", "맑은 고딕"),
        ("Batang", "바탕"),
        ("Gulim", "굴림"),
        ("Dotum", "돋움"),
    };
}
