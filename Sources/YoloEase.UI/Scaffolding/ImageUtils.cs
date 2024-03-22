using System.Drawing;
using System.Drawing.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;

namespace YoloEase.UI.Scaffolding;

public static class ImageUtils
{
    [Obsolete("Leaks memory")]
    public static string ConvertImageToBase64(FileInfo fileInfo)
    {
        using var image = Image.FromFile(fileInfo.FullName);
        using var m = new MemoryStream();

        image.Save(m, ImageFormat.Png);
        var imageBytes = m.ToArray();

        // Convert byte[] to Base64 String
        var base64String = Convert.ToBase64String(imageBytes);

        return $"data:image/png;base64,{base64String}";
    }

    public static Size GetImageSize(FileInfo fileInfo)
    {
        var directories = ImageMetadataReader.ReadMetadata(fileInfo.FullName);
        foreach (var directory in directories)
        {
            // Handle TIFF (they commonly use Exif)
            if (directory is ExifIfd0Directory || directory is ExifSubIfdDirectory)
            {
                if (directory.ContainsTag(ExifDirectoryBase.TagImageWidth) && directory.ContainsTag(ExifDirectoryBase.TagImageHeight))
                {
                    var width = directory.GetInt32(ExifDirectoryBase.TagImageWidth);
                    var height = directory.GetInt32(ExifDirectoryBase.TagImageHeight);
                    return new Size(width, height);
                }
            }
            // Handle JPEG
            else if (directory is JpegDirectory)
            {
                var width = directory.GetInt32(JpegDirectory.TagImageWidth);
                var height = directory.GetInt32(JpegDirectory.TagImageHeight);
                return new Size(width, height);
            }
            // Handle PNG
            else if (directory is PngDirectory)
            {
                var width = directory.GetInt32(PngDirectory.TagImageWidth);
                var height = directory.GetInt32(PngDirectory.TagImageHeight);
                return new Size(width, height);
            }
            // Handle GIF
            else if (directory is GifHeaderDirectory)
            {
                var width = directory.GetInt32(GifHeaderDirectory.TagImageWidth);
                var height = directory.GetInt32(GifHeaderDirectory.TagImageHeight);
                return new Size(width, height);
            }
            // Handle BMP
            else if (directory is BmpHeaderDirectory)
            {
                var width = directory.GetInt32(BmpHeaderDirectory.TagImageWidth);
                var height = directory.GetInt32(BmpHeaderDirectory.TagImageHeight);
                return new Size(width, height);
            }
        }

        throw new NotSupportedException($"Provided file type is not supported({fileInfo.Extension}): {fileInfo.FullName}");
    }
}