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
        /// <para>
        /// 抓屏失败不抛出，返回的是那张没画上内容的位图（也就是全黑）。理由有两条：显示器
        /// 休眠、会话锁定或远程会话下 <c>CopyFromScreen</c> 会失败，而它成功时在休眠的显示器
        /// 上返回的也是纯黑，两者对用户是同一件事；更要紧的是这个方法在
        /// <c>SetStyleToForm</c> 里被调用，抛出去等于「抓不到屏就打不开样式设置对话框」。
        /// 背景纯属装饰，不值这个代价。
        /// </para>
        /// <para>
        /// 也因此调用方拿到的位图尺寸必然等于 <paramref name="size"/>，四个面板的绘制代码
        /// 可以继续按它的宽高给示例图定位。
        /// </para>
        /// </summary>
        public static Image Capture(Size size)
        {
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }

            var captured = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);

            try
            {
                using (var graphics = Graphics.FromImage(captured))
                {
                    graphics.CopyFromScreen(Point.Empty, Point.Empty, captured.Size);
                }
            }
            catch (Exception)
            {
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

            var replacement = Capture(size);
            if (replacement == null)
            {
                return backdrop;
            }

            backdrop.Dispose();

            return replacement;
        }
    }
}
