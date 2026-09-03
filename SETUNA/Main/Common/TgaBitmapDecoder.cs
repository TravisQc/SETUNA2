using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SETUNA.Main
{
    /// <summary>
    /// Decodes the TGA variants used by SETUNA without pulling a WPF BitmapSource
    /// dependency into the WinForms process. The decoder deliberately returns a
    /// detached 32-bit bitmap so callers can close the input stream immediately.
    /// </summary>
    internal static class TgaBitmapDecoder
    {
        public static Bitmap Decode(Stream input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            using (var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true))
            {
                var idLength = reader.ReadByte();
                var colorMapType = reader.ReadByte();
                var imageType = reader.ReadByte();
                var colorMapFirst = reader.ReadUInt16();
                var colorMapLength = reader.ReadUInt16();
                var colorMapEntryBits = reader.ReadByte();
                reader.ReadUInt16(); // X origin
                reader.ReadUInt16(); // Y origin
                var width = reader.ReadUInt16();
                var height = reader.ReadUInt16();
                var pixelBits = reader.ReadByte();
                var descriptor = reader.ReadByte();

                if (width == 0 || height == 0 || width > 32767 || height > 32767)
                {
                    throw new InvalidDataException("TGA dimensions are invalid.");
                }

                if (idLength != 0)
                {
                    ReadExactly(reader, idLength);
                }

                var isColorMapped = imageType == 1 || imageType == 9;
                var isTrueColor = imageType == 2 || imageType == 10;
                var isGray = imageType == 3 || imageType == 11;
                var isRle = imageType == 9 || imageType == 10 || imageType == 11;
                if ((!isColorMapped && !isTrueColor && !isGray)
                    || (isColorMapped && colorMapType != 1)
                    || (!isColorMapped && colorMapType != 0)
                    || (descriptor & 0x0F) > 8)
                {
                    throw new NotSupportedException("Unsupported TGA image type or descriptor.");
                }

                Color[] colorMap = null;
                if (isColorMapped)
                {
                    if (colorMapLength == 0 || !IsSupportedColorBits(colorMapEntryBits))
                    {
                        throw new InvalidDataException("TGA color map is invalid.");
                    }

                    colorMap = new Color[colorMapLength];
                    for (var i = 0; i < colorMap.Length; i++)
                    {
                        colorMap[i] = ReadColor(reader, colorMapEntryBits, descriptor);
                    }
                }

                if ((isTrueColor && !IsSupportedColorBits(pixelBits))
                    || (isColorMapped && pixelBits != 8 && pixelBits != 16)
                    || (isGray && pixelBits != 8 && pixelBits != 16))
                {
                    throw new NotSupportedException("Unsupported TGA pixel format.");
                }

                var pixels = new byte[checked(width * height * 4)];
                var pixelIndex = 0;
                while (pixelIndex < width * height)
                {
                    var count = 1;
                    var run = false;
                    if (isRle)
                    {
                        var packet = reader.ReadByte();
                        count = (packet & 0x7F) + 1;
                        run = (packet & 0x80) != 0;
                    }

                    if (count > width * height - pixelIndex)
                    {
                        throw new InvalidDataException("TGA RLE packet exceeds the image bounds.");
                    }

                    var color = ReadPixel(reader, pixelBits, isColorMapped, isGray, colorMap, colorMapFirst, descriptor);
                    for (var i = 0; i < count; i++)
                    {
                        if (i != 0 && !run)
                        {
                            color = ReadPixel(reader, pixelBits, isColorMapped, isGray, colorMap, colorMapFirst, descriptor);
                        }

                        var sourceX = pixelIndex % width;
                        var sourceY = pixelIndex / width;
                        var targetX = (descriptor & 0x10) != 0 ? width - sourceX - 1 : sourceX;
                        var targetY = (descriptor & 0x20) != 0 ? sourceY : height - sourceY - 1;
                        var target = (targetY * width + targetX) * 4;
                        pixels[target] = color.B;
                        pixels[target + 1] = color.G;
                        pixels[target + 2] = color.R;
                        pixels[target + 3] = color.A;
                        pixelIndex++;
                    }
                }

                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    for (var row = 0; row < height; row++)
                    {
                        Marshal.Copy(pixels, row * width * 4, IntPtr.Add(data.Scan0, row * data.Stride), width * 4);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return bitmap;
            }
        }

        static Color ReadPixel(
            BinaryReader reader,
            int pixelBits,
            bool colorMapped,
            bool gray,
            Color[] colorMap,
            int colorMapFirst,
            byte descriptor)
        {
            if (colorMapped)
            {
                var index = pixelBits == 8 ? reader.ReadByte() : reader.ReadUInt16();
                var mapIndex = index - colorMapFirst;
                if (mapIndex < 0 || mapIndex >= colorMap.Length)
                {
                    throw new InvalidDataException("TGA color-map index is outside the map.");
                }

                return colorMap[mapIndex];
            }

            if (gray)
            {
                var value = reader.ReadByte();
                var alpha = pixelBits == 16 ? reader.ReadByte() : (byte)255;
                return Color.FromArgb(alpha, value, value, value);
            }

            return ReadColor(reader, pixelBits, descriptor);
        }

        static Color ReadColor(BinaryReader reader, int bits, byte descriptor)
        {
            if (bits == 15 || bits == 16)
            {
                var packed = reader.ReadUInt16();
                var r = (packed >> 10) & 0x1F;
                var g = (packed >> 5) & 0x1F;
                var b = packed & 0x1F;
                var a = bits == 16 && (descriptor & 0x0F) > 0
                    ? ((packed & 0x8000) != 0 ? 255 : 0)
                    : 255;
                return Color.FromArgb(a, Expand5(r), Expand5(g), Expand5(b));
            }

            var blue = reader.ReadByte();
            var green = reader.ReadByte();
            var red = reader.ReadByte();
            var alpha = bits == 32 ? reader.ReadByte() : (byte)255;
            return Color.FromArgb(alpha, red, green, blue);
        }

        static bool IsSupportedColorBits(int bits)
        {
            return bits == 15 || bits == 16 || bits == 24 || bits == 32;
        }

        static byte Expand5(int value)
        {
            return (byte)((value << 3) | (value >> 2));
        }

        static byte[] ReadExactly(BinaryReader reader, int count)
        {
            var data = reader.ReadBytes(count);
            if (data.Length != count)
            {
                throw new EndOfStreamException("TGA ended before the image data started.");
            }

            return data;
        }
    }
}
