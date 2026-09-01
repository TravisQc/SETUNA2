using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace SETUNA.Main.Common
{
    /// <summary>
    /// 样式设置对话框预览框里那块「假桌面」：从屏幕左上角抓一块与预览框等大的图当背景，
    /// 让预览中的示例贴图看起来像浮在桌面上。
    /// <para>
    /// 为什么值得单独一个类：这张位图的尺寸必须始终等于预览框的尺寸。四个面板
    /// （不透明度、紧凑、边距、旋转）的绘制代码都是「把它按原尺寸画在 (0,0)，再按它的宽高
    /// 把示例图居中」，预览框一旦比它大，右下就会露出一条没画到的空白，示例图也会偏离中心。
    /// 原来只在 <c>SetStyleToForm</c> 里抓一次就够，是因为预览框的尺寸此后不再变；跨显示器
    /// 重排会按 DPI 改预览框的尺寸，于是重排之后还得重抓。
    /// </para>
    /// </summary>
    static class PreviewBackdrop
    {
        /// <summary>
        /// 抓一块 <paramref name="size"/> 大小的屏幕内容。任一边不为正时返回 <c>null</c>。
        /// </summary>
        public static Image Capture(Size size)
        {
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }

            var captured = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);

            using (var graphics = Graphics.FromImage(captured))
            {
                graphics.CopyFromScreen(Point.Empty, Point.Empty, captured.Size);
            }

            return captured;
        }

        /// <summary>
        /// 让 <paramref name="backdrop"/> 跟上 <paramref name="size"/>：尺寸已经相符就原样返回，
        /// 否则重抓一张并释放旧的。
        /// <para>
        /// 传入 <c>null</c> 时返回 <c>null</c>。面板的无参构造不调
        /// <c>InitializeComponent</c>，那种状态下既没有预览框也没有背景，不该凭空造出一张。
        /// </para>
        /// </summary>
        public static Image Resize(Image backdrop, Size size)
        {
            if (backdrop == null || backdrop.Size == size)
            {
                return backdrop;
            }

            Image replacement;
            try
            {
                replacement = Capture(size);
            }
            catch (Exception)
            {
                // 抓屏不是总能成功——显示器休眠时 CopyFromScreen 会失败或者只返回纯黑。
                // 背景纯属装饰，而这里位于 WM_DPICHANGED 的处理过程中，抛出去会让整次重排
                // 半途而废，所以失败就留着旧的那张。
                return backdrop;
            }

            if (replacement == null)
            {
                return backdrop;
            }

            backdrop.Dispose();

            return replacement;
        }
    }
}
