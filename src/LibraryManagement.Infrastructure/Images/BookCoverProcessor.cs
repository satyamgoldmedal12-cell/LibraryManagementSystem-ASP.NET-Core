using SkiaSharp;

namespace LibraryManagement.Infrastructure.Images;

public sealed class BookCoverProcessor : IBookCoverProcessor
{
    public BookCover Process(BookCoverUpload upload)
    {
        if (upload.Content.Length is 0 or > BookCoverLimits.MaxUploadBytes)
            throw new BusinessException("Cover images must be between 1 byte and 2 MB.");
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png"))
            throw new BusinessException("Upload a JPEG or PNG cover image.");
        using var data = SKData.CreateCopy(upload.Content);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png)
            || (extension == ".png") != (codec.EncodedFormat == SKEncodedImageFormat.Png))
            throw new BusinessException("The cover contents do not match a valid JPEG or PNG image.");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || info.Width > BookCoverLimits.MaxDimension
            || info.Height > BookCoverLimits.MaxDimension || (long)info.Width * info.Height > BookCoverLimits.MaxPixels)
            throw new BusinessException("Cover dimensions must not exceed 4096 pixels per side or 12 megapixels.");
        using var pixels = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(pixels.Info, pixels.GetPixels()) != SKCodecResult.Success)
            throw new BusinessException("The cover image is damaged or incomplete.");
        var rotated = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = rotated ? info.Height : info.Width; var height = rotated ? info.Width : info.Height;
        var scale = Math.Min(1d, 1024d / Math.Max(width, height));
        using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale))));
        if (surface is null) throw new BusinessException("The cover image could not be processed.");
        var canvas = surface.Canvas; canvas.Clear(SKColors.White); canvas.Scale((float)scale);
        var matrix = Orientation(codec.EncodedOrigin, info.Width, info.Height);
        canvas.Concat(matrix);
        using var image = SKImage.FromBitmap(pixels);
        canvas.DrawImage(image, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear));
        using var normalized = surface.Snapshot();
        using var jpeg = normalized.Encode(SKEncodedImageFormat.Jpeg, 85);
        if (jpeg is null || jpeg.Size > BookCoverLimits.MaxUploadBytes) throw new BusinessException("The cover image could not be compressed. Try a smaller image.");
        // Persist only reconstructed pixels: original filenames, EXIF/GPS data, and trailing payloads are discarded.
        return new BookCover { Content = jpeg.ToArray(), ContentType = "image/jpeg" };
    }
    private static SKMatrix Orientation(SKEncodedOrigin origin, int w, int h)
    {
        var (sx, kx, tx, ky, sy, ty) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1, 0, w, 0, 1, 0),
            SKEncodedOrigin.BottomRight => (-1, 0, w, 0, -1, h),
            SKEncodedOrigin.BottomLeft => (1, 0, 0, 0, -1, h),
            SKEncodedOrigin.LeftTop => (0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => (0, -1, h, 1, 0, 0),
            SKEncodedOrigin.RightBottom => (0, -1, h, -1, 0, w),
            SKEncodedOrigin.LeftBottom => (0, 1, 0, -1, 0, w),
            _ => (1, 0, 0, 0, 1, 0)
        };
        return new SKMatrix { ScaleX = sx, SkewX = kx, TransX = tx, SkewY = ky, ScaleY = sy, TransY = ty, Persp2 = 1 };
    }
}
