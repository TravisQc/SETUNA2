using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Common.Tests
{
    /// <summary>
    /// Pins the bounds-safety and the recognition rules of the magic-byte sniffer.
    /// The SVG and PSD branches previously guarded with <c>Length &gt; 2</c> while
    /// reading index 3, so a 3-byte input threw <see cref="IndexOutOfRangeException"/>.
    /// </summary>
    [TestClass]
    public class ImageFormatDetectionTests
    {
        static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        [TestMethod]
        public void ShortBuffersReturnUnknownWithoutThrowing()
        {
            for (var length = 0; length <= 20; length++)
            {
                var buffer = new byte[length];

                // All-zero prefixes must not be mistaken for a format either, apart
                // from the ICO signature which legitimately starts with zeros.
                var type = ImageUtils.GetImageType(buffer);

                Assert.IsTrue(
                    type == ImageType.Unknown || type == ImageType.ICO || type == ImageType.TGA,
                    "length " + length + " produced " + type);
            }
        }

        [TestMethod]
        public void ThreeByteBufferDoesNotThrow()
        {
            // The exact regression: '<sv' and '8BP' are 3 bytes and used to read index 3.
            Assert.AreEqual(ImageType.Unknown, ImageUtils.GetImageType(Encoding.ASCII.GetBytes("<sv")));
            Assert.AreEqual(ImageType.Unknown, ImageUtils.GetImageType(Encoding.ASCII.GetBytes("8BP")));
        }

        [TestMethod]
        public void EmptyAndNullBuffersReturnUnknown()
        {
            Assert.AreEqual(ImageType.Unknown, ImageUtils.GetImageType(new byte[0]));
            Assert.AreEqual(ImageType.Unknown, ImageUtils.GetImageType(null));
        }

        [TestMethod]
        public void EverySupportedFormatIsRecognised()
        {
            Assert.AreEqual(ImageType.PNG, ImageUtils.GetImageType(Pad(PngSignature, 32)));
            Assert.AreEqual(ImageType.JPEG, ImageUtils.GetImageType(Pad(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, 32)));
            Assert.AreEqual(ImageType.GIF, ImageUtils.GetImageType(Encoding.ASCII.GetBytes("GIF89a").Concat(new byte[26]).ToArray()));
            Assert.AreEqual(ImageType.PSD, ImageUtils.GetImageType(Encoding.ASCII.GetBytes("8BPS").Concat(new byte[28]).ToArray()));
            Assert.AreEqual(ImageType.ICO, ImageUtils.GetImageType(Pad(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 }, 32)));
            Assert.AreEqual(ImageType.SVG, ImageUtils.GetImageType(Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")));
        }

        [TestMethod]
        public void WebPRequiresBothRiffAndWebpMarkers()
        {
            var webp = Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 0x10, 0x00, 0x00, 0x00 })
                .Concat(Encoding.ASCII.GetBytes("WEBP")).Concat(new byte[16]).ToArray();
            Assert.AreEqual(ImageType.WEBP, ImageUtils.GetImageType(webp));

            // "WEBP" at offset 8 but no RIFF container: not a WebP file.
            var withoutRiff = new byte[8].Concat(Encoding.ASCII.GetBytes("WEBP")).Concat(new byte[16]).ToArray();
            Assert.AreNotEqual(ImageType.WEBP, ImageUtils.GetImageType(withoutRiff));
        }

        [TestMethod]
        public void APngMustCarryItsLeadingSignatureByte()
        {
            // The old check looked only at bytes 1..3 ("PNG"), so any file whose
            // second through fourth bytes spelled PNG was decoded as one.
            var fakePng = new byte[] { 0x00, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
                .Concat(new byte[24]).ToArray();

            Assert.AreNotEqual(ImageType.PNG, ImageUtils.GetImageType(fakePng));
        }

        [TestMethod]
        public void ATruncatedPngSignatureIsNotReportedAsPng()
        {
            Assert.AreNotEqual(ImageType.PNG, ImageUtils.GetImageType(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        }

        [TestMethod]
        public void AnSvgWithAnXmlDeclarationIsRecognised()
        {
            var svg = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                + "<!-- exported -->\n"
                + "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n"
                + "<svg width=\"10\" height=\"10\" xmlns=\"http://www.w3.org/2000/svg\"></svg>";

            Assert.AreEqual(ImageType.SVG, ImageUtils.GetImageType(Encoding.UTF8.GetBytes(svg)));
        }

        [TestMethod]
        public void AnSvgWithUppercaseElementNameIsRecognised()
        {
            Assert.AreEqual(ImageType.SVG, ImageUtils.GetImageType(Encoding.ASCII.GetBytes("<SVG></SVG>")));
        }

        [TestMethod]
        public void PlainTextIsNotMistakenForSvg()
        {
            Assert.AreEqual(ImageType.Unknown, ImageUtils.GetImageType(Encoding.ASCII.GetBytes("just some notes about svg files")));
        }

        [TestMethod]
        public void ABinarySignatureWinsOverALaterSvgLikeSequence()
        {
            // A PNG whose payload happens to contain "<svg" must stay a PNG.
            var png = PngSignature.Concat(Encoding.ASCII.GetBytes("...<svg...")).Concat(new byte[16]).ToArray();

            Assert.AreEqual(ImageType.PNG, ImageUtils.GetImageType(png));
        }

        [TestMethod]
        public void DetectionNeverThrowsForArbitraryInput()
        {
            // Deterministic sweep over every single-byte prefix at several lengths.
            foreach (var length in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 17 })
            {
                for (var first = 0; first < 256; first++)
                {
                    var buffer = new byte[length];
                    buffer[0] = (byte)first;
                    if (length > 3)
                    {
                        buffer[3] = (byte)first;
                    }

                    ImageUtils.GetImageType(buffer);
                }
            }
        }

        static byte[] Pad(byte[] prefix, int totalLength)
        {
            var result = new byte[totalLength];
            Array.Copy(prefix, result, prefix.Length);
            return result;
        }
    }
}
