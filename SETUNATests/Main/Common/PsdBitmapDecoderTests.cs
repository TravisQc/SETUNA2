using System;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNATests.Main.Common
{
    [TestClass]
    public class PsdBitmapDecoderTests
    {
        [TestMethod]
        public void DecodesFlattenedRgbPsdIntoOwnedBitmap()
        {
            using (var stream = new MemoryStream(CreateRawRgbPsd()))
            using (var bitmap = PsdBitmapDecoder.Decode(stream))
            {
                Assert.AreEqual(new Size(2, 1), bitmap.Size);
                Assert.AreEqual(Color.FromArgb(255, 255, 0, 0), bitmap.GetPixel(0, 0));
                Assert.AreEqual(Color.FromArgb(255, 0, 255, 0), bitmap.GetPixel(1, 0));
            }
        }

        static byte[] CreateRawRgbPsd()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 0x38, 0x42, 0x50, 0x53 }); // 8BPS
                WriteUInt16(writer, 1);
                writer.Write(new byte[6]);
                WriteUInt16(writer, 3); // channels
                WriteUInt32(writer, 1); // height
                WriteUInt32(writer, 2); // width
                WriteUInt16(writer, 8); // depth
                WriteUInt16(writer, 3); // RGB
                WriteUInt32(writer, 0); // color mode data
                WriteUInt32(writer, 0); // image resources
                WriteUInt32(writer, 0); // layer and mask data
                WriteUInt16(writer, 0); // raw compression
                writer.Write(new byte[]
                {
                    255, 0, // red plane
                    0, 255, // green plane
                    0, 0 // blue plane
                });
                writer.Flush();
                return stream.ToArray();
            }
        }

        static void WriteUInt16(BinaryWriter writer, ushort value)
        {
            writer.Write(new[] { (byte)(value >> 8), (byte)value });
        }

        static void WriteUInt32(BinaryWriter writer, uint value)
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
