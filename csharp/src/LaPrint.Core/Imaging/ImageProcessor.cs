// 이미지 후처리 — 알파가 없으면 가장자리 최빈색을 배경으로 보고 플러드필 투명화 (imaging.js).
using SkiaSharp;

namespace LaPrint.Core.Imaging;

/// <summary>그림 파일 바이트 → 처리된 비트맵.</summary>
public static class ImageProcessor
{
    public static SKBitmap Process(byte[] data, bool autoTransparent = true, int tolerance = 30)
        => throw new NotImplementedException("ImageProcessor.Process — 아직 구현되지 않았습니다");
}
