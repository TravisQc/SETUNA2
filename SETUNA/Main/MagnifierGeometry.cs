using System.Drawing;

namespace SETUNA.Main
{
    /// <summary>
    /// 放大镜的定位与取景几何。抽出来是为了让「窗口在任何光标位置都完整落在目标
    /// 屏幕内、且不压住光标」这一性质可以脱离 UI 直接验证——原实现只是把窗口钉在
    /// 屏幕的左上角和右下角之间来回翻。
    /// </summary>
    public static class MagnifierGeometry
    {
        /// <summary>放大倍率。取景矩形按此倍率整数放大，因此不会出现宽窄不一的像素块。</summary>
        public const int Magnification = 4;

        /// <summary>窗口边缘与光标之间的间隙（设计像素，高 DPI 下由调用方按缩放折算）。</summary>
        public const int DefaultGap = 24;

        /// <summary>
        /// 窗口左上角坐标。优先放在光标右下方；某一维度放不下就翻到光标另一侧；
        /// 最后整体钳制进 <paramref name="screen"/>。
        /// <para>
        /// 用 Bounds 而不是 WorkingArea：截图范围本身就是整块屏幕，任务栏所在区域
        /// 也在可截范围内，用工作区会让窗口在屏幕底部无谓地提前翻转。
        /// </para>
        /// </summary>
        public static Point WindowLocation(Point cursor, Size window, Rectangle screen, int gap)
        {
            var x = cursor.X + gap;
            if (x + window.Width > screen.Right)
            {
                x = cursor.X - gap - window.Width;
            }

            var y = cursor.Y + gap;
            if (y + window.Height > screen.Bottom)
            {
                y = cursor.Y - gap - window.Height;
            }

            return new Point(
                ClampAxis(x, screen.Left, screen.Right - window.Width),
                ClampAxis(y, screen.Top, screen.Bottom - window.Height));
        }

        /// <summary>
        /// 取景矩形的边长：目标区域除以倍率、向下取整、至少 1 像素。向下取整是
        /// <see cref="DestinationRectangle"/> 的整数放大结果不超出目标区域的前提。
        /// </summary>
        public static Size ViewportSize(Size destination, int magnification)
        {
            var factor = AtLeastOne(magnification);

            return new Size(
                AtLeastOne(destination.Width / factor),
                AtLeastOne(destination.Height / factor));
        }

        /// <summary>
        /// 放大后画面在目标区域内的位置：尺寸恰为取景矩形的整数倍，居中放置，除不尽
        /// 的余量变成四周的细边。倍率因此严格等于 <paramref name="magnification"/>，
        /// 不会出现 246/61 那样 4.03 倍下宽窄不一的像素块。
        /// </summary>
        public static Rectangle DestinationRectangle(Size destination, Size viewport, int magnification)
        {
            var factor = AtLeastOne(magnification);
            var width = viewport.Width * factor;
            var height = viewport.Height * factor;

            return new Rectangle(
                (destination.Width - width) / 2,
                (destination.Height - height) / 2,
                width,
                height);
        }

        /// <summary>以 <paramref name="cursor"/> 为中心的取景矩形（快照坐标系）。</summary>
        public static Rectangle SourceRectangle(Point cursor, Size viewport)
        {
            return new Rectangle(
                cursor.X - viewport.Width / 2,
                cursor.Y - viewport.Height / 2,
                viewport.Width,
                viewport.Height);
        }

        /// <summary>
        /// 把取景矩形裁进快照范围，并算出它对应的目标矩形。越界时只画交集那部分，
        /// 其余留给调用方用背景色填——原实现直接拿可能为负的坐标去取屏，屏幕边缘
        /// 取到的内容不可预测。无交集时返回 <see cref="MagnifiedRegion.Empty"/>。
        /// </summary>
        public static MagnifiedRegion Clip(Rectangle source, Size snapshot, Rectangle destination, int magnification)
        {
            var factor = AtLeastOne(magnification);
            var clipped = Rectangle.Intersect(source, new Rectangle(Point.Empty, snapshot));

            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                return MagnifiedRegion.Empty;
            }

            return new MagnifiedRegion(
                clipped,
                new Rectangle(
                    destination.X + (clipped.X - source.X) * factor,
                    destination.Y + (clipped.Y - source.Y) * factor,
                    clipped.Width * factor,
                    clipped.Height * factor));
        }

        /// <summary>
        /// 钳制放在最后一步，意味着屏幕装不下「窗口 + 间隙」时，「窗口完整可见」赢过
        /// 「保持间隙」乃至「不压住光标」；屏幕连窗口本身都装不下时（<paramref name="max"/>
        /// 会小于 <paramref name="min"/>）靠左上对齐，让溢出落在远侧而不是产生负向越界。
        /// </summary>
        static int ClampAxis(int value, int min, int max)
        {
            if (max < min)
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        static int AtLeastOne(int value)
        {
            return value < 1 ? 1 : value;
        }
    }

    /// <summary>一次放大绘制的源矩形（快照坐标系）与目标矩形（缓冲区坐标系）。</summary>
    public struct MagnifiedRegion
    {
        public MagnifiedRegion(Rectangle source, Rectangle destination)
        {
            Source = source;
            Destination = destination;
        }

        public Rectangle Source { get; }

        public Rectangle Destination { get; }

        public bool IsEmpty => Source.Width <= 0 || Source.Height <= 0;

        public static MagnifiedRegion Empty => new MagnifiedRegion(Rectangle.Empty, Rectangle.Empty);
    }
}
