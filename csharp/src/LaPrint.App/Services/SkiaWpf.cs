// SKBitmap ↔ WPF BitmapSource 변환 (LaVis Services/SkiaWpf.cs 와 같은 동작 — 픽셀 복사만 WritePixels 로).
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace LaPrint.App.Services;

public static class SkiaWpf
{
    /// <summary>SKBitmap 을 Frozen BitmapSource(Bgra32, 96dpi) 로 복사한다.</summary>
    public static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        SKBitmap source = bitmap;
        if (bitmap.ColorType != SKColorType.Bgra8888)
        {
            source = new SKBitmap(bitmap.Width, bitmap.Height,
                                  SKColorType.Bgra8888, SKAlphaType.Premul);
            bitmap.CopyTo(source, SKColorType.Bgra8888);
        }
        var writeable = new WriteableBitmap(source.Width, source.Height, 96, 96,
                                            PixelFormats.Bgra32, null);
        writeable.Lock();
        try
        {
            writeable.WritePixels(new Int32Rect(0, 0, source.Width, source.Height),
                                  source.GetPixels(), source.RowBytes * source.Height, source.RowBytes);
        }
        finally { writeable.Unlock(); }
        if (!ReferenceEquals(source, bitmap)) source.Dispose();
        writeable.Freeze();
        return writeable;
    }
}
