using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Common.Tests
{
    [TestClass]
    public class TgaBitmapDecoderTests
    {
        [TestMethod]
        public void DecodesUncompressedBgraAndTopLeftOrigin()
        {
            var tga = CreateTga(2, 1, 32, 2, 0x28, new byte[]
            {
                0x1E, 0x14, 0x0A, 0x80, // B G R A
                0xC8, 0xB4, 0xA0, 0x40
            });

            using (var bitmap = Decode(tga))
            {
                Assert.AreEqual(Color.FromArgb(0x80, 0x0A, 0x14, 0x1E), bitmap.GetPixel(0, 0));
                Assert.AreEqual(Color.FromArgb(0x40, 0xA0, 0xB4, 0xC8), bitmap.GetPixel(1, 0));
            }
        }

        [TestMethod]
        public void HonorsBottomRightOriginAndRlePackets()
        {
            // Two pixels in a bottom-right-origin row. The RLE packet stores them in
            // source order; the decoder must reverse both axes into bitmap coordinates.
            var tga = CreateTga(2, 1, 24, 10, 0x10, new byte[]
            {
                0x81, 0xFF, 0x00, 0x00 // one RLE packet, blue pixel
            });

            using (var bitmap = Decode(tga))
            {
                Assert.AreEqual(Color.Blue.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
                Assert.AreEqual(Color.Blue.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            }
        }

        [TestMethod]
        public void DecodesSixteenBitAlphaWhenDescriptorDeclaresIt()
        {
            // 5-5-5-1: red with alpha on, then green with alpha off.
            var tga = CreateTga(2, 1, 16, 2, 0x21, new byte[]
            {
                0x00, 0xFC,
                0xE0, 0x03
            });

            using (var bitmap = Decode(tga))
            {
                Assert.AreEqual(255, bitmap.GetPixel(0, 0).A);
                Assert.AreEqual(0, bitmap.GetPixel(1, 0).A);
                Assert.IsTrue(bitmap.GetPixel(0, 0).R > 240);
                Assert.IsTrue(bitmap.GetPixel(1, 0).G > 240);
            }
        }

        [TestMethod]
        public void RejectsUnsupportedColorMapEntryBits()
        {
            var tga = CreateTga(1, 1, 8, 1, 0x20, new byte[] { 0 });
            tga[1] = 1; // color-map present
            tga[5] = 1; // color-map length
            tga[7] = 18; // unsupported 18-bit entry

            using (var stream = new MemoryStream(tga))
            {
                Assert.ThrowsException<InvalidDataException>(() => TgaBitmapDecoder.Decode(stream));
            }
        }

        static Bitmap Decode(byte[] bytes)
        {
            return TgaBitmapDecoder.Decode(new MemoryStream(bytes));
        }

        static byte[] CreateTga(int width, int height, int pixelBits, int imageType, int descriptor, byte[] pixels)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write((byte)0); // id length
                writer.Write((byte)0); // no color map by default
                writer.Write((byte)imageType);
                writer.Write((ushort)0); // color map first
                writer.Write((ushort)0); // color map length
                writer.Write((byte)0); // color map entry bits
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)width);
                writer.Write((ushort)height);
                writer.Write((byte)pixelBits);
                writer.Write((byte)descriptor);
                writer.Write(pixels);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
