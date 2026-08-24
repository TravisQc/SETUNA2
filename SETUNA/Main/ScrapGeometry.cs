using System.Drawing;

namespace SETUNA.Main
{
    /// <summary>
    /// 截图窗口的缩放边界与尺寸换算。抽出来是为了让「任何缩放取值都不会产生
    /// 零或负数的宽高」这一性质可以直接验证——下界原本是 -200。
    /// </summary>
    public static class ScrapGeometry
    {
        /// <summary>缩放比例下界（百分比）。必须为正，否则宽高会变成 0 或负数。</summary>
        public const int MinimumScale = 1;

        /// <summary>缩放比例上界（百分比）。</summary>
        public const int MaximumScale = 200;

        public static int ClampScale(int scale)
        {
            if (scale < MinimumScale)
            {
                return MinimumScale;
            }

            return scale > MaximumScale ? MaximumScale : scale;
        }

        /// <summary>
        /// 按 <paramref name="scale"/>（百分比）换算窗口外框尺寸，含两侧边距。
        /// <paramref name="scale"/> 会先被钳制到合法区间，换算后的每一维至少 1 像素——
        /// 仅钳制比例是不够的：80 像素高的图在 scale=1、无边距时
        /// <c>(int)(80 * 0.01f)</c> 会截断成 0。
        /// </summary>
        public static Size ScaledOuterSize(Size imageSize, int scale, int padding)
        {
            var effectiveScale = ClampScale(scale);

            return new Size(
                ScaleDimension(imageSize.Width, effectiveScale) + padding * 2,
                ScaleDimension(imageSize.Height, effectiveScale) + padding * 2);
        }

        static int ScaleDimension(int length, int effectiveScale)
        {
            var scaled = (int)(length * (effectiveScale / 100f));

            return scaled < 1 ? 1 : scaled;
        }

        /// <summary>
        /// 绘制图像的目标区域：客户区尺寸减去两侧边距。
        /// 透明与非透明背景模式必须使用同一套计算。
        /// </summary>
        public static Rectangle ImageDestination(int clientWidth, int clientHeight, int padding)
        {
            return new Rectangle(padding, padding, clientWidth - padding * 2, clientHeight - padding * 2);
        }
    }
}
