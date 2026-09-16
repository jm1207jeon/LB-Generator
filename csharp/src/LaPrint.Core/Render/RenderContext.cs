// 렌더 문맥 — 치환기·이미지 슬롯·배경·DPI. 화면·PDF·ZPL 이 같은 문맥 구조를 쓴다.
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.Core.Render;

/// <summary>한 번의 렌더에 필요한 데이터와 옵션.</summary>
public sealed class RenderContext
{
    public Fields Fields { get; set; } = new();
    public DbRow? Row { get; set; }

    /// <summary>플레이스홀더 치환기.</summary>
    public Func<string, string> ResolveText { get; set; } = t => t;

    public IReadOnlyList<LabelObject> Objects { get; set; } = Array.Empty<LabelObject>();

    /// <summary>출력용(true)이면 편집 장식 없이 그린다. Core 는 false 여도 장식을 그리지 않는다.</summary>
    public bool ForExport { get; set; }

    public bool IncludeBg { get; set; } = true;
    public SKBitmap? Background { get; set; }
    public double Dpi { get; set; } = 300;

    /// <summary>이미지 객체 id → 비트맵 (슬롯 캐시). 없으면 null.</summary>
    public Func<string, SKBitmap?> ImageOf { get; set; } = _ => null;
}
