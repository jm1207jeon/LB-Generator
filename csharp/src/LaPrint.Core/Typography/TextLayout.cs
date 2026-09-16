// 조판 결과 — 줄 목록과 세로 배치, 넘침 여부. 단위는 mm.
namespace LaPrint.Core.Typography;

/// <summary>한 줄의 배치. XMm 는 정렬 후 시작 x, VisWMm 는 잉크 폭, SpaceExtra 는 양끝 정렬 시 공백당 추가 폭.</summary>
public sealed record LayoutLine(string Text, double XMm, double WMm, double VisWMm, double SpaceExtra);

/// <summary>텍스트 객체 하나의 조판 결과.</summary>
public sealed record TextLayout(IReadOnlyList<LayoutLine> Lines, double SizePt, double FontMm, double LineHMm, double TotalHMm,
                                double StartYMm, double HScale, bool OverflowX, bool OverflowY, bool Shrunk, double AscMm, double DescMm);
