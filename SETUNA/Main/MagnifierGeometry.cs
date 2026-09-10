using System.Collections.Generic;
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

        /// <summary>中心标记外框的外环相对中心方块的外扩量，单位是设备像素。</summary>
        public const int MarkerOuterRingOffset = 2;

        /// <summary>中心标记外框的内环相对中心方块的外扩量，单位是设备像素。</summary>
        public const int MarkerInnerRingOffset = 1;

        /// <summary>十字线的粗细，单位是设备像素。粗 2px：1px 深色 + 1px 浅色相邻。</summary>
        public const int MarkerThickness = 2;

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

        /// <summary>
        /// 中心标记的填充矩形集合。标记指示的对象是放大画面的中心像素——也就是
        /// <see cref="SourceRectangle"/> 用作取景中心的同一个源像素，因此它标记的
        /// 正是光标实际所指的位置。
        /// <para>
        /// 产出规则（全部相对 <paramref name="destination"/>，坐标单位是设备像素）：
        /// <list type="bullet">
        /// <item>中心方块本身不产出任何矩形，内部保持画面原样，标记绝不遮盖被指示的像素；</item>
        /// <item>方块外沿画内浅外深两圈环（各 4 条边），把目标像素圈出来；</item>
        /// <item>十字线由 1 深 1 浅两条相邻的 1px 线组成，从 <paramref name="destination"/>
        /// 的边缘延伸到外环为止，因此横竖线在中心断开、不穿过方块；</item>
        /// <item>所有矩形在返回前与 <paramref name="destination"/> 求交，空的结果丢弃。
        /// 这一步同时兜住「不画到放大区域之外的余量细边上」和「缓冲区小到装不下标记」
        /// 两种情况。</item>
        /// </list>
        /// 中心方块的下标必须与 <see cref="SourceRectangle"/> 里的 <c>viewport/2</c>
        /// 共用同一表达式，否则取景尺寸为偶数时标记会差一格。
        /// </summary>
        public static CrosshairMark[] CrosshairMarks(Rectangle destination, Size viewport, int magnification)
        {
            var factor = AtLeastOne(magnification);

            var centerX = viewport.Width / 2;
            var centerY = viewport.Height / 2;

            var block = new Rectangle(
                destination.X + centerX * factor,
                destination.Y + centerY * factor,
                factor,
                factor);
            var footprint = Rectangle.Inflate(block, MarkerOuterRingOffset, MarkerOuterRingOffset);

            var marks = new List<CrosshairMark>();

            var inner = Rectangle.Inflate(block, MarkerInnerRingOffset, MarkerInnerRingOffset);
            AddRing(marks, inner, dark: false, destination);
            AddRing(marks, footprint, dark: true, destination);

            var lineHeight = 1;
            var top = block.Y + (factor - MarkerThickness) / 2;

            AddSegment(marks, destination.Left, footprint.Left, top, lineHeight, destination, dark: false);
            AddSegment(marks, destination.Left, footprint.Left, top + 1, lineHeight, destination, dark: true);

            AddSegment(marks, footprint.Right, destination.Right, top, lineHeight, destination, dark: false);
            AddSegment(marks, footprint.Right, destination.Right, top + 1, lineHeight, destination, dark: true);

            var left = block.X + (factor - MarkerThickness) / 2;
            var columnWidth = 1;

            AddSegment(marks, destination.Top, footprint.Top, left, columnWidth, destination, dark: false, vertical: true);
            AddSegment(marks, destination.Top, footprint.Top, left + 1, columnWidth, destination, dark: true, vertical: true);

            AddSegment(marks, footprint.Bottom, destination.Bottom, left, columnWidth, destination, dark: false, vertical: true);
            AddSegment(marks, footprint.Bottom, destination.Bottom, left + 1, columnWidth, destination, dark: true, vertical: true);

            return marks.ToArray();
        }

        /// <summary>把 <paramref name="ring"/> 的四条边作为标记矩形加入 <paramref name="marks"/>。</summary>
        static void AddRing(List<CrosshairMark> marks, Rectangle ring, bool dark, Rectangle destination)
        {
            AddIfValid(marks, new Rectangle(ring.X, ring.Y, ring.Width, 1), dark, destination);
            AddIfValid(marks, new Rectangle(ring.X, ring.Bottom - 1, ring.Width, 1), dark, destination);
            AddIfValid(marks, new Rectangle(ring.X, ring.Y + 1, 1, ring.Height - 2), dark, destination);
            AddIfValid(marks, new Rectangle(ring.Right - 1, ring.Y + 1, 1, ring.Height - 2), dark, destination);
        }

        /// <summary>
        /// 在主轴方向从 <paramref name="start"/> 延到 <paramref name="end"/> 的一条标记矩形。
        /// 水平时 <paramref name="offset"/> 是行号、长度取 <paramref name="length"/>；垂直时相反。
        /// </summary>
        static void AddSegment(
            List<CrosshairMark> marks, int start, int end, int offset, int length, Rectangle destination,
            bool dark, bool vertical = false)
        {
            var rectangle = vertical
                ? new Rectangle(offset, start, length, end - start)
                : new Rectangle(start, offset, end - start, length);

            AddIfValid(marks, rectangle, dark, destination);
        }

        /// <summary>标记矩形与 <paramref name="destination"/> 求交，交非空才加入。空结果（缓冲区
        /// 过小、或矩形落在放大区域之外）丢弃，绘制因此永不越界。</summary>
        static void AddIfValid(List<CrosshairMark> marks, Rectangle rectangle, bool dark, Rectangle destination)
        {
            var clipped = Rectangle.Intersect(rectangle, destination);

            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                return;
            }

            marks.Add(new CrosshairMark(clipped, dark));
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

    /// <summary>
    /// 中心标记的一块填充矩形。<see cref="Bounds"/> 是它在缓冲区坐标系里的位置，
    /// <see cref="Dark"/> 为 true 时画深色（黑）、false 时画浅色（白）。深浅相邻的
    /// 两条 1px 线保证任意底色下至少一侧有对比。
    /// </summary>
    public struct CrosshairMark
    {
        public CrosshairMark(Rectangle bounds, bool dark)
        {
            Bounds = bounds;
            Dark = dark;
        }

        public Rectangle Bounds { get; }

        public bool Dark { get; }
    }
}
