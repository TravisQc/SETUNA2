using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SETUNA.Main.Common
{
    /// <summary>
    /// 按 <see cref="PictureBoxSizeMode.Zoom"/> 的规则等比缩放居中绘制位图，但用高质量重采样。
    /// <para>
    /// 为什么需要它：<c>PictureBox</c> 自己算好目标矩形之后直接调 <c>Graphics.DrawImage</c>，
    /// 不动插值设置，用的就是 GDI+ 的默认值——低质量双线性，外加默认的
    /// <see cref="PixelOffsetMode"/>。缩放比越接近 1 越难看：选项窗体左下角的 OptionBG 是
    /// 170x370，控件在 100% 显示器上是 266x360，等比缩放比 0.973——几乎是原尺寸，却每个像素
    /// 都和邻居混进去一点，整张图看着失焦。
    /// </para>
    /// <para>
    /// 跨显示器重排本身没有丢精度（实测窗口外框 1069x746、客户区 1063x700、字号 9pt 往返四次
    /// 分毫不差），图片发虚只来自这一步重采样，所以在缩放比最接近 1 的那台显示器上最明显。
    /// 实测同一目标尺寸下换成 <see cref="InterpolationMode.HighQualityBicubic"/>，边缘能量
    /// 从 2.13 升到 2.62（100% 下的 0.973 倍），1.52 升到 1.77（175% 下的 1.703 倍）。
    /// </para>
    /// <para>
    /// 重采样做干净变不出原图没有的细节：170x370 的素材在 175% 下要放大 1.7 倍，那一档要锐利
    /// 只能换更高分辨率的素材。
    /// </para>
    /// </summary>
    public static class SmoothImage
    {
        /// <summary>
        /// 等比缩放到 <paramref name="bounds"/> 内并居中。取整方式与
        /// <see cref="PictureBoxSizeMode.Zoom"/> 逐字一致（比例取两轴较小者、尺寸截断、位置整除），
        /// 因此换用本类只改重采样质量，不动排版。任一边不为正时返回 <see cref="Rectangle.Empty"/>。
        /// </summary>
        public static Rectangle Fit(Size image, Size bounds)
        {
            if (image.Width <= 0 || image.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return Rectangle.Empty;
            }

            var ratio = Math.Min(
                bounds.Width / (float)image.Width,
                bounds.Height / (float)image.Height);

            var size = new Size((int)(image.Width * ratio), (int)(image.Height * ratio));

            return new Rectangle(
                (bounds.Width - size.Width) / 2,
                (bounds.Height - size.Height) / 2,
                size.Width,
                size.Height);
        }

        /// <summary>
        /// 把 <paramref name="image"/> 按 <see cref="Fit"/> 的结果高质量画进 <paramref name="bounds"/>。
        /// <para>
        /// 用 <see cref="WrapMode.TileFlipXY"/> 的 <see cref="ImageAttributes"/>：双三次插值在边缘
        /// 要取源图之外的样本，不指定环绕方式时 GDI+ 按透明处理，四周会多出一圈半透明的边。
        /// </para>
        /// <para>
        /// 用完把两个模式还回去：<c>Graphics</c> 来自 <c>PaintEventArgs</c>，同一次绘制里后面
        /// 可能还有别的控件用它。
        /// </para>
        /// </summary>
        public static void DrawFitted(Graphics g, Image image, Size bounds)
        {
            if (g == null || image == null)
            {
                return;
            }

            var target = Fit(image.Size, bounds);
            if (target.IsEmpty)
            {
                return;
            }

            var previousInterpolation = g.InterpolationMode;
            var previousPixelOffset = g.PixelOffsetMode;

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            try
            {
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(
                        image,
                        target,
                        0, 0, image.Width, image.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }
            }
            finally
            {
                g.InterpolationMode = previousInterpolation;
                g.PixelOffsetMode = previousPixelOffset;
            }
        }

        /// <summary>
        /// 接管 <paramref name="box"/> 的图像绘制，一律按 <see cref="Fit"/>（即 Zoom）的规则画。
        /// <para>
        /// 必须把图从控件上取下来：<c>PictureBox.OnPaint</c> 先用默认插值画一遍才轮到
        /// <c>Paint</c> 事件，留在原处就是画两遍，而且第一遍正是发虚的那一遍。取下之后
        /// <c>SizeMode</c> 不再起作用，缩放规则由本类给出。
        /// </para>
        /// <para>
        /// 位图由资源持有，本方法只借用引用、不接管生命周期，因此控件释放时不需要额外收尾；
        /// 事件订阅的目标是控件自己，也不会让控件被外部对象留住。
        /// </para>
        /// </summary>
        public static void Attach(PictureBox box)
        {
            if (box == null || box.Image == null)
            {
                return;
            }

            var image = box.Image;
            box.Image = null;
            box.Paint += (sender, e) => DrawFitted(e.Graphics, image, box.ClientSize);
        }
    }
}
