using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Windows.Media.Imaging;
using Svg;

namespace SETUNA.Main
{
    class URLUtils
    {
        public const string OriginURL = "http://www.clearunit.com/clearup/setuna2/";

        public const string NewURL = "https://github.com/TravisQc/SETUNA2";
    }


    static class BitmapUtils
    {
        public static Bitmap ScaleToSize(this Bitmap bitmap, int width, int height)
        {
            if (bitmap.Width == width && bitmap.Height == height)
            {
                return bitmap;
            }

            var scaledBitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(scaledBitmap))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(bitmap, 0, 0, width, height);
            }

            return scaledBitmap;
        }

        public static Bitmap FromPath(string path)
        {
            if (File.Exists(path))
            {

                byte[] buffer = null;
                MemoryStream stream = null;
                Bitmap bitmap = null;

                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        buffer = new byte[fs.Length];
                        stream = new MemoryStream(buffer);
                        fs.CopyTo(stream);
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    var imageType = ImageUtils.GetImageType(buffer);
                    switch (imageType)
                    {
                        case ImageType.PNG:
                            using (var source = new Bitmap(stream))
                            {
                                bitmap = new Bitmap(source);
                            }
                            break;
                        case ImageType.WEBP:
                            using (var webp = new WebPWrapper.WebP())
                            {
                                bitmap = webp.Decode(buffer);
                            }
                            break;
                        case ImageType.SVG:
                            bitmap = SvgDocument.Open<SvgDocument>(stream).Draw();
                            break;
                        case ImageType.PSD:
                            var psdFile = new System.Drawing.PSD.PsdFile();
                            psdFile.Load(path);
                            bitmap = System.Drawing.PSD.ImageDecoder.DecodeImage(psdFile);
                            break;
                        case ImageType.ICO:
                            using (var icon = new Icon(path))
                            {
                                bitmap = icon.ToBitmap();
                            }
                            break;
                        case ImageType.TGA:
                            using (var reader = new BinaryReader(stream))
                            {
                                var image = new TgaLib.TgaImage(reader);
                                bitmap = image.GetBitmap().ToBitmap();
                            }
                            break;
                        default:
                            using (var source = new Bitmap(stream))
                            {
                                bitmap = new Bitmap(source);
                            }
                            break;
                    }

                    return bitmap;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
                finally
                {
                    if (stream != null)
                    {
                        stream.Dispose();
                    }
                }
            }

            return null;
        }

        public static void DownloadImage(string url, Action<Bitmap> finished)
        {
            // 下载临时文件放系统 Temp，不放缓存目录：缓存目录是用户数据，
            // 且进程在下载中途死亡时残留文件会留在那里。
            var filePath = Path.Combine(Path.GetTempPath(), string.Format("SETUNA_TEMP_{0}_{1}.png", DateTime.Now.Ticks, Math.Abs(url.GetHashCode())));
            var client = new WebClient();
            client.DownloadFileCompleted += (s, e) =>
            {
                Bitmap bitmap = null;

                try
                {
                    if (e.Cancelled)
                    {
                        Console.WriteLine("Image download was cancelled: " + url);
                    }
                    else if (e.Error != null)
                    {
                        Console.WriteLine("Image download failed: " + e.Error);
                    }
                    else
                    {
                        bitmap = BitmapUtils.FromPath(filePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Downloaded image could not be read: " + ex);
                }
                finally
                {
                    DeleteTemporaryFile(filePath);
                    client.Dispose();
                }

                finished?.Invoke(bitmap);
            };
            client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.141 Safari/537.36";

            try
            {
                client.DownloadFileAsync(new Uri(url), filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Image download could not be started: " + ex);
                DeleteTemporaryFile(filePath);
                client.Dispose();
                finished?.Invoke(null);
            }
        }

        private static void DeleteTemporaryFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Temporary image file could not be deleted: " + ex);
            }
        }

        public static Bitmap ToBitmap(this BitmapSource source)
        {
            Bitmap bitmap;
            using (var outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(source));
                enc.Save(outStream);
                outStream.Position = 0;
                using (var image = new Bitmap(outStream))
                {
                    bitmap = new Bitmap(image);
                }
            }
            return bitmap;
        }
    }

    public static class ImageUtils
    {
        // SVG 是文本格式，元素不一定出现在文件开头（可能先有 XML 声明、注释、DOCTYPE），
        // 因此在开头这段范围内扫描而不是只看固定偏移。
        const int SvgScanLength = 1024;

        public static ImageType GetImageType(byte[] imageBuffer)
        {
            if (imageBuffer == null)
            {
                return ImageType.Unknown;
            }

            // 每个分支的长度校验都由签名长度推导（见 Matches），
            // 不再手写 Length 阈值——原来 SVG 和 PSD 写的是 Length > 2 却读到索引 3，
            // 长度恰好为 3 的输入会抛 IndexOutOfRangeException。
            if (Matches(imageBuffer, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
            {
                return ImageType.PNG;
            }

            // FF D8 FF 覆盖 JFIF、Exif 和裸 SOI 三种变体；
            // 原来只认偏移 6 处的 "JFIF"，Exif 相机照片会落到 Unknown。
            if (Matches(imageBuffer, 0, 0xFF, 0xD8, 0xFF))
            {
                return ImageType.JPEG;
            }

            // RIFF....WEBP
            if (Matches(imageBuffer, 0, 0x52, 0x49, 0x46, 0x46)
                && Matches(imageBuffer, 8, 0x57, 0x45, 0x42, 0x50))
            {
                return ImageType.WEBP;
            }

            if (Matches(imageBuffer, 0, 0x47, 0x49, 0x46))
            {
                return ImageType.GIF;
            }

            // "8BPS"
            if (Matches(imageBuffer, 0, 0x38, 0x42, 0x50, 0x53))
            {
                return ImageType.PSD;
            }

            if (Matches(imageBuffer, 0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00))
            {
                return ImageType.ICO;
            }

            if (LooksLikeSvg(imageBuffer))
            {
                return ImageType.SVG;
            }

            if (TGAUtils.IsTGA(imageBuffer))
            {
                return ImageType.TGA;
            }

            return ImageType.Unknown;
        }

        /// <summary>
        /// 判断 <paramref name="buffer"/> 从 <paramref name="offset"/> 起是否匹配 <paramref name="signature"/>。
        /// 越界一律返回 false，绝不抛异常。
        /// </summary>
        static bool Matches(byte[] buffer, int offset, params byte[] signature)
        {
            if (offset < 0 || offset + signature.Length > buffer.Length)
            {
                return false;
            }

            for (var i = 0; i < signature.Length; i++)
            {
                if (buffer[offset + i] != signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 在文件开头一段范围内查找 <c>&lt;svg</c> 元素，因此带 XML 声明、注释或
        /// DOCTYPE 的 SVG 也能被识别。
        /// </summary>
        static bool LooksLikeSvg(byte[] buffer)
        {
            var limit = Math.Min(buffer.Length, SvgScanLength);
            var needle = new[] { '<', 's', 'v', 'g' };

            for (var i = 0; i + needle.Length <= limit; i++)
            {
                var matched = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    var current = char.ToLowerInvariant((char)buffer[i + j]);
                    if (current != needle[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }
    }

    static class TGAUtils
    {
        public static bool IsTGA(byte[] imageBuffer)
        {
            if (imageBuffer.Length > 16)
            {
                var imageType = imageBuffer[2];
                var colorMapDepth = imageBuffer[7];
                var pixelDepth = imageBuffer[16];

                switch (imageType)
                {
                    case 1:
                    case 9:
                        if (colorMapDepth - 15 <= 1 || colorMapDepth == 24 || colorMapDepth == 32)
                        {
                            return true;
                        }
                        break;
                    case 2:
                    case 10:
                        if (pixelDepth - 15 <= 1 || pixelDepth == 24 || pixelDepth == 32)
                        {
                            return true;
                        }
                        break;
                    case 3:
                    case 11:
                        if (pixelDepth == 8)
                        {
                            return true;
                        }
                        break;
                }
            }

            return false;
        }
    }

    public enum ImageType
    {
        Unknown,
        JPEG,
        PNG,
        WEBP,
        GIF,
        SVG,
        PSD,
        ICO,
        TGA,
    }
}

