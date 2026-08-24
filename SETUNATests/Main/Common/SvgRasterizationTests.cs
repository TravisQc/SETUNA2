using System;
using System.Drawing;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Common.Tests
{
    /// <summary>
    /// Pins the SVG rasterisation contract of <see cref="BitmapUtils.FromPath"/>.
    /// The renderer was bumped from Svg 2.2.1 (a net35 assembly with no dependencies)
    /// to Svg 3.4.8, so these tests exist to keep the observable behaviour stable
    /// across that jump and any future one: a well-formed document yields a usable
    /// bitmap, and a document the renderer cannot handle yields null rather than
    /// propagating the renderer's exception to the caller.
    /// </summary>
    [TestClass]
    public class SvgRasterizationTests
    {
        string workingDirectory;

        [TestInitialize]
        public void CreateWorkingDirectory()
        {
            // 每个测试一个独立临时目录：与用户的真实缓存目录和配置隔离。
            workingDirectory = Path.Combine(
                Path.GetTempPath(),
                "SETUNATests_Svg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
        }

        [TestCleanup]
        public void RemoveWorkingDirectory()
        {
            try
            {
                if (workingDirectory != null && Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, true);
                }
            }
            catch (IOException)
            {
                // 清理失败不应让测试结果变红，临时目录会由系统回收。
            }
        }

        /// <summary>
        /// 把 SVG 文本写入临时目录，再经生产解码入口加载。
        /// 走 <see cref="BitmapUtils.FromPath"/> 而不是直接调 Svg 库，
        /// 这样魔数探测和异常兜底都在被测范围内。
        /// </summary>
        Bitmap Load(string svg)
        {
            var path = Path.Combine(workingDirectory, "input.svg");
            File.WriteAllText(path, svg, new UTF8Encoding(false));
            return BitmapUtils.FromPath(path);
        }

        static void AssertUsableBitmap(Bitmap bitmap, string because)
        {
            Assert.IsNotNull(bitmap, because);
            using (bitmap)
            {
                Assert.IsTrue(
                    bitmap.Width > 0 && bitmap.Height > 0,
                    string.Format("{0}：期望正的宽高，实际 {1}x{2}", because, bitmap.Width, bitmap.Height));
            }
        }

        [TestMethod]
        public void ADocumentWithExplicitDimensionsIsRasterised()
        {
            var bitmap = Load(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"80\">" +
                "<rect width=\"120\" height=\"80\" fill=\"#c53\"/></svg>");

            Assert.IsNotNull(bitmap, "带显式尺寸的 SVG 应当被光栅化");
            using (bitmap)
            {
                Assert.AreEqual(120, bitmap.Width, "宽度应取自 width 属性");
                Assert.AreEqual(80, bitmap.Height, "高度应取自 height 属性");
            }
        }

        [TestMethod]
        public void ADocumentBehindAnXmlDeclarationAndCommentIsRasterised()
        {
            // 魔数探测要跨过 XML 声明和注释才能看到 <svg，光栅化要读到完整文档。
            var bitmap = Load(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!-- 根元素之前的注释 -->\n" +
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"48\">" +
                "<circle cx=\"32\" cy=\"24\" r=\"20\" fill=\"#3aa\"/></svg>");

            AssertUsableBitmap(bitmap, "带 XML 声明与注释的 SVG 应当被光栅化");
        }

        [TestMethod]
        public void ADocumentStyledThroughCssSelectorsIsRasterised()
        {
            // Svg 3.x 用 ExCSS 解析 <style>；Svg 2.2.1 走的是另一套实现。
            // 这个用例保证换解析器之后带 CSS 的文档依然能渲染。
            var bitmap = Load(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"60\">" +
                "<style>.bg{fill:#3aa;} #dot{fill:#c53;}</style>" +
                "<rect class=\"bg\" width=\"100\" height=\"60\"/>" +
                "<circle id=\"dot\" cx=\"50\" cy=\"30\" r=\"15\"/></svg>");

            AssertUsableBitmap(bitmap, "含 CSS 样式块的 SVG 应当被光栅化");
        }

        [TestMethod]
        public void ATruncatedDocumentYieldsNullWithoutThrowing()
        {
            // Svg 3.4.8 的 SvgDocument.Open 对未闭合的文档抛 XmlException，
            // FromPath 必须把它兜住并返回空结果。
            var bitmap = Load("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"60\" height=\"60\"><rect width=\"60\"");

            Assert.IsNull(bitmap, "XML 结构不完整的 SVG 应当返回空结果，而不是把 XmlException 抛给调用方");
        }

        [TestMethod]
        public void ADocumentWithoutIntrinsicSizeDoesNotThrow()
        {
            // 规格只要求「返回位图或返回 null，都不得抛异常」，不锁定具体哪一种：
            // 这属于渲染库的实现细节，跨大版本可能变化。
            // Svg 3.4.8 实测按内容边界推断尺寸并返回位图。
            var bitmap = Load(
                "<svg xmlns=\"http://www.w3.org/2000/svg\">" +
                "<rect width=\"50\" height=\"40\" fill=\"#c53\"/></svg>");

            if (bitmap != null)
            {
                AssertUsableBitmap(bitmap, "无内在尺寸的 SVG 若返回位图，宽高必须为正");
            }
        }

        [TestMethod]
        public void ADocumentSizedInPercentagesDoesNotThrow()
        {
            var bitmap = Load(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100%\" height=\"100%\">" +
                "<rect width=\"50\" height=\"40\" fill=\"#3aa\"/></svg>");

            if (bitmap != null)
            {
                AssertUsableBitmap(bitmap, "百分比尺寸的 SVG 若返回位图，宽高必须为正");
            }
        }

        [TestMethod]
        public void RasterisationStartsFromTheBeginningOfTheStream()
        {
            // FromPath 先把整个文件读进缓冲区做魔数探测，再把同一个流交给 SVG 分支。
            // 若探测后没有把流复位，解析会从非零偏移开始并丢掉开头的元素。
            // 用一个头部较长的文档放大这个失败模式：漏掉开头就渲染不出预期尺寸。
            var padding = new string(' ', 512);
            var bitmap = Load(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + padding +
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"77\" height=\"55\">" +
                "<rect width=\"77\" height=\"55\" fill=\"#3aa\"/></svg>");

            Assert.IsNotNull(bitmap, "经魔数探测后 SVG 解析应当读到完整文档");
            using (bitmap)
            {
                Assert.AreEqual(77, bitmap.Width, "宽度错误说明文档开头被跳过");
                Assert.AreEqual(55, bitmap.Height, "高度错误说明文档开头被跳过");
            }
        }
    }
}
