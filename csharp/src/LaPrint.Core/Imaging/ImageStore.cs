// 이미지 폴더 — 정확한 이름 → 대소문자 무시 매칭, 처리된 비트맵 LRU 캐시 150.
using SkiaSharp;

namespace LaPrint.Core.Imaging;

/// <summary>이미지 폴더(UNC 가능)에서 파일을 읽고 처리 결과를 캐시한다.</summary>
public sealed class ImageStore
{
    public ImageStore(string folder)
    {
        Folder = folder ?? "";
    }

    /// <summary>이미지 폴더 경로.</summary>
    public string Folder { get; }

    public byte[] Read(string fileName)
        => throw new NotImplementedException("ImageStore.Read — 아직 구현되지 않았습니다");

    public SKBitmap GetProcessed(string fileName, bool autoTransparent, int tolerance)
        => throw new NotImplementedException("ImageStore.GetProcessed — 아직 구현되지 않았습니다");

    /// <summary>폴더 목록·비트맵 캐시를 비운다.</summary>
    public void Invalidate()
        => throw new NotImplementedException("ImageStore.Invalidate — 아직 구현되지 않았습니다");

    public IReadOnlyList<string> ListImages()
        => throw new NotImplementedException("ImageStore.ListImages — 아직 구현되지 않았습니다");
}
