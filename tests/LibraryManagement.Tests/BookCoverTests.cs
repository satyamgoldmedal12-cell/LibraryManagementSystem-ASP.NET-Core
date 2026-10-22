using LibraryManagement.Infrastructure.Images;
using SkiaSharp;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class BookCoverTests
{
    internal static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 40, int height = 60, SKColor? color = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(color ?? SKColors.DarkCyan);
        using var image = surface.Snapshot(); using var data = image.Encode(format, 85); return data.ToArray();
    }
    [Theory]
    [InlineData("cover.png", SKEncodedImageFormat.Png)]
    [InlineData("cover.JPEG", SKEncodedImageFormat.Jpeg)]
    public void Valid_covers_are_reconstructed_as_JPEG(string name, SKEncodedImageFormat format)
    {
        var original = Image(format); var result = new BookCoverProcessor().Process(new(original, name));
        Assert.Equal("image/jpeg", result.ContentType);
        using var data = SKData.CreateCopy(result.Content); using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat); Assert.Equal(40, codec.Info.Width); Assert.Equal(60, codec.Info.Height);
        if (format == SKEncodedImageFormat.Png) Assert.NotEqual(original, result.Content);
    }
    [Theory]
    [InlineData("cover.svg")]
    [InlineData("cover.exe")]
    [InlineData("cover.jpg.html")]
    public void Unsupported_extensions_are_rejected(string name) => Assert.Throws<BusinessException>(() => new BookCoverProcessor().Process(new(Image(), name)));
    [Fact]
    public void MIME_and_filename_cannot_disguise_an_executable_or_HTML() => Assert.Throws<BusinessException>(() => new BookCoverProcessor().Process(new(System.Text.Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), "cover.png")));
    [Fact]
    public void Extension_must_match_the_decoded_format() => Assert.Throws<BusinessException>(() => new BookCoverProcessor().Process(new(Image(), "cover.jpg")));
    [Fact]
    public void Oversized_dimensions_are_rejected_before_pixel_decoding() => Assert.Throws<BusinessException>(() => new BookCoverProcessor().Process(new(Image(width: 4097, height: 1), "cover.png")));
    [Fact]
    public void Images_are_resized_and_appended_payloads_are_discarded()
    {
        var bytes = Image(width: 2000, height: 1000).Concat(System.Text.Encoding.UTF8.GetBytes("UNTRUSTED_TRAILING_PAYLOAD")).ToArray();
        var result = new BookCoverProcessor().Process(new(bytes, "../../cover.png"));
        using var data = SKData.CreateCopy(result.Content); using var codec = SKCodec.Create(data);
        Assert.Equal(1024, codec.Info.Width); Assert.Equal(512, codec.Info.Height);
        Assert.DoesNotContain("UNTRUSTED_TRAILING_PAYLOAD", System.Text.Encoding.Latin1.GetString(result.Content));
    }
    [Fact]
    public void Truncated_images_are_rejected()
    {
        var bytes = Image(); Assert.Throws<BusinessException>(() => new BookCoverProcessor().Process(new(bytes[..(bytes.Length / 2)], "cover.png")));
    }
}
