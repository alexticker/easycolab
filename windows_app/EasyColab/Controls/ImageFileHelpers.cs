using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace EasyColab.Controls;

/// <summary>Image file helpers used by <see cref="FrameDropWell"/> (and available to view models).</summary>
public static class ImageFileHelpers
{
    /// <summary>Extensions offered by the picker (Swift <c>allowedContentTypes = [.image]</c>).</summary>
    public static readonly string[] PickerExtensions =
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".heif", ".ico",
    };

    /// <summary>Equivalent of <c>NSImage(contentsOf:) != nil</c>: true when WIC can decode the file.</summary>
    public static async Task<bool> IsLoadableImageAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ras = fs.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);
            return decoder.PixelWidth > 0 && decoder.PixelHeight > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Port of Swift <c>encodeImageToBase64(_:)</c>: JPEG at compression factor 0.92, raw base64 (no <c>data:</c> prefix).
    /// Respects EXIF orientation; alpha is flattened. Returns null when the file cannot be decoded.
    /// </summary>
    public static async Task<string?> EncodeImageFileToJpegBase64Async(string? path, double quality = 0.92)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ras = fs.AsRandomAccessStream())
            {
                var decoder = await BitmapDecoder.CreateAsync(ras);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);

                using var outStream = new InMemoryRandomAccessStream();
                var props = new BitmapPropertySet
                {
                    { "ImageQuality", new BitmapTypedValue((float)quality, Windows.Foundation.PropertyType.Single) },
                };
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream, props);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();

                bytes = new byte[outStream.Size];
                outStream.Seek(0);
                using var reader = new DataReader(outStream);
                await reader.LoadAsync((uint)outStream.Size);
                reader.ReadBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }
}
