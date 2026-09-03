using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace SETUNA.Main.Runtime
{
    /// <summary>
    /// 自检用的最小样本，每种格式一份。
    /// <para>
    /// 手写容器头而不是随产品附带样本文件：<see cref="SelfTest"/> 要在「exe 旁边什么都没有」
    /// 的目录里跑。这些字节同时是各格式头部的第二份独立表述——它们不引用任何解码器，因此
    /// 两边同时写错才会通过。
    /// </para>
    /// </summary>
    internal static class FormatSamples
    {
        /// <summary>每个像素都与邻居不同，所以一次错误的重采样也会改变解码结果。</summary>
        public static Bitmap Bitmap(int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    bitmap.SetPixel(x, y, Color.FromArgb((x * 61) % 256, (y * 97) % 256, (x + y * 7) % 256));
                }
            }

            return bitmap;
        }

        public static byte[] Png()
        {
            using (var bitmap = Bitmap(4, 3))
            using (var buffer = new MemoryStream())
            {
                bitmap.Save(buffer, ImageFormat.Png);
                return buffer.ToArray();
            }
        }

        public static byte[] WebP()
        {
            using (var bitmap = Bitmap(3, 2))
            using (var webp = new WebPWrapper.WebP())
            {
                return webp.EncodeLossless(bitmap);
            }
        }

        /// <summary>带 XML 声明的 SVG，因为格式嗅探要在开头一段里找 <c>&lt;svg</c>，不只看第一个字节。</summary>
        public static byte[] Svg()
        {
            return Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"6\">"
                    + "<rect x=\"0\" y=\"0\" width=\"8\" height=\"6\" fill=\"#3366cc\"/>"
                    + "<circle cx=\"4\" cy=\"3\" r=\"2\" fill=\"#ffcc00\"/>"
                    + "</svg>");
        }

        /// <summary>2x1 的未压缩 RGB PSD：8BPS 头 + 三段长度为 0 的区块 + 每通道一行原始数据。</summary>
        public static byte[] Psd()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 0x38, 0x42, 0x50, 0x53 }); // "8BPS"
                BigEndian16(writer, 1); // version
                writer.Write(new byte[6]); // reserved
                BigEndian16(writer, 3); // channels
                BigEndian32(writer, 1); // height
                BigEndian32(writer, 2); // width
                BigEndian16(writer, 8); // bits per channel
                BigEndian16(writer, 3); // RGB colour mode
                BigEndian32(writer, 0); // colour mode data
                BigEndian32(writer, 0); // image resources
                BigEndian32(writer, 0); // layer and mask information
                BigEndian16(writer, 0); // raw compression

                // Planar: the whole red channel, then green, then blue.
                writer.Write(new byte[] { 0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00 });

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>
        /// 2x2 的未压缩 32bpp DIB 图标。刻意不用 Vista 起支持的 PNG 内嵌形式：
        /// <see cref="Icon.ToBitmap"/> 对 DIB 的路径才是所有 Windows 版本上都一样的那条。
        /// </summary>
        public static byte[] Ico()
        {
            const int Width = 2;
            const int Height = 2;
            const int HeaderSize = 40;
            const int PixelBytes = Width * Height * 4;
            const int MaskBytes = Height * 4; // 每行按 4 字节对齐，2 个像素占 1 字节
            const int ImageBytes = HeaderSize + PixelBytes + MaskBytes;

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // ICONDIR：保留 0、类型 1（图标）、1 张图。格式嗅探认的就是这六个字节。
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)1);

                // ICONDIRENTRY
                writer.Write((byte)Width);
                writer.Write((byte)Height);
                writer.Write((byte)0); // palette entries
                writer.Write((byte)0); // reserved
                writer.Write((ushort)1); // planes
                writer.Write((ushort)32); // bits per pixel
                writer.Write(ImageBytes);
                writer.Write(22); // 6 字节 ICONDIR + 16 字节 ICONDIRENTRY

                // BITMAPINFOHEADER：高度是图像加掩码，所以写两倍。
                writer.Write(HeaderSize);
                writer.Write(Width);
                writer.Write(Height * 2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(new byte[HeaderSize - 16]);

                // XOR 位图，BGRA，自下而上。
                for (var i = 0; i < Width * Height; i++)
                {
                    writer.Write(new byte[] { (byte)(i * 40), (byte)(i * 70), (byte)(i * 90), 0xFF });
                }

                // AND 掩码：全 0 表示整张不透明。
                writer.Write(new byte[MaskBytes]);

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>2x2 的未压缩 24bpp TGA（image type 2），像素自下而上按 BGR 排列。</summary>
        public static byte[] Tga()
        {
            var header = new byte[18];
            header[2] = 2; // uncompressed true-colour
            header[12] = 2; // width low byte
            header[14] = 2; // height low byte
            header[16] = 24; // bits per pixel

            var pixels = new byte[]
            {
                0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00,
                0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF
            };

            var sample = new byte[header.Length + pixels.Length];
            header.CopyTo(sample, 0);
            pixels.CopyTo(sample, header.Length);

            return sample;
        }

        static void BigEndian16(BinaryWriter writer, ushort value)
        {
            writer.Write(new[] { (byte)(value >> 8), (byte)value });
        }

        static void BigEndian32(BinaryWriter writer, uint value)
        {
            writer.Write(new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            });
        }
    }
}
