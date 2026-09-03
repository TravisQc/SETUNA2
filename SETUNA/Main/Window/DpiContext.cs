using System;
using System.Drawing;

namespace SETUNA.Main.Window
{
    /// <summary>
    /// Explicit boundary between WinForms logical UI units and physical screen pixels.
    /// Coordinates are scaled in one double-precision operation and rounded away from zero.
    /// </summary>
    public sealed class DpiContext
    {
        public const int BaseDpi = 96;

        public DpiContext(MonitorSnapshot monitor)
        {
            Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        }

        public MonitorSnapshot Monitor { get; }

        public bool IsAvailable => Monitor.IsAvailable;

        /// <summary>The monitor DPI on each axis. A value of zero means unavailable.</summary>
        public int DpiX => Monitor.DpiX;

        public int DpiY => Monitor.DpiY;

        /// <summary>
        /// Convenience scale for symmetric-DPI callers. Use <see cref="Monitor.ScaleX"/>
        /// and <see cref="Monitor.ScaleY"/> when the axes differ.
        /// </summary>
        public double ScaleFactor => Monitor.ScaleX;

        public static DpiContext FromDpi(int dpiX, int dpiY)
        {
            return new DpiContext(new MonitorSnapshot(
                IntPtr.Zero,
                string.Empty,
                Rectangle.Empty,
                Rectangle.Empty,
                dpiX,
                dpiY,
                false));
        }

        public int LogicalToPhysicalLengthX(int logical)
        {
            return Scale(logical, Monitor.ScaleX);
        }

        public int LogicalToPhysicalLengthY(int logical)
        {
            return Scale(logical, Monitor.ScaleY);
        }

        public int PhysicalToLogicalLengthX(int physical)
        {
            return Scale(physical, Monitor.IsAvailable ? 1d / Monitor.ScaleX : 0d);
        }

        public int PhysicalToLogicalLengthY(int physical)
        {
            return Scale(physical, Monitor.IsAvailable ? 1d / Monitor.ScaleY : 0d);
        }

        public Point LogicalToPhysical(Point logical)
        {
            return new Point(
                LogicalToPhysicalCoordinate(logical.X, Monitor.ScaleX),
                LogicalToPhysicalCoordinate(logical.Y, Monitor.ScaleY));
        }

        public Point PhysicalToLogical(Point physical)
        {
            return new Point(
                PhysicalToLogicalCoordinate(physical.X, Monitor.ScaleX),
                PhysicalToLogicalCoordinate(physical.Y, Monitor.ScaleY));
        }

        public Size LogicalToPhysical(Size logical)
        {
            return new Size(
                LogicalToPhysicalLengthX(logical.Width),
                LogicalToPhysicalLengthY(logical.Height));
        }

        public Size PhysicalToLogical(Size physical)
        {
            return new Size(
                PhysicalToLogicalLengthX(physical.Width),
                PhysicalToLogicalLengthY(physical.Height));
        }

        public Rectangle LogicalToPhysical(Rectangle logical)
        {
            var location = LogicalToPhysical(logical.Location);
            var size = LogicalToPhysical(logical.Size);
            return new Rectangle(location, size);
        }

        public Rectangle PhysicalToLogical(Rectangle physical)
        {
            var location = PhysicalToLogical(physical.Location);
            var size = PhysicalToLogical(physical.Size);
            return new Rectangle(location, size);
        }

        public bool TryLogicalToPhysical(Point logical, out Point physical)
        {
            if (!IsAvailable)
            {
                physical = default(Point);
                return false;
            }

            physical = LogicalToPhysical(logical);
            return true;
        }

        public bool TryPhysicalToLogical(Point physical, out Point logical)
        {
            if (!IsAvailable)
            {
                logical = default(Point);
                return false;
            }

            logical = PhysicalToLogical(physical);
            return true;
        }

        public bool TryLogicalToPhysical(Rectangle logical, out Rectangle physical)
        {
            if (!IsAvailable || logical.Width <= 0 || logical.Height <= 0)
            {
                physical = default(Rectangle);
                return false;
            }

            physical = LogicalToPhysical(logical);
            return true;
        }

        public bool TryPhysicalToLogical(Rectangle physical, out Rectangle logical)
        {
            if (!IsAvailable || physical.Width <= 0 || physical.Height <= 0)
            {
                logical = default(Rectangle);
                return false;
            }

            logical = PhysicalToLogical(physical);
            return true;
        }

        /// <summary>Scales a value with the requested factor and AwayFromZero rounding.</summary>
        public static int Scale(int value, double factor)
        {
            if (factor <= 0d || double.IsNaN(factor) || double.IsInfinity(factor))
            {
                return value;
            }

            var rounded = Math.Round(value * factor, MidpointRounding.AwayFromZero);
            if (rounded > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (rounded < int.MinValue)
            {
                return int.MinValue;
            }

            return (int)rounded;
        }

        /// <summary>
        /// 一个 DPI 值能不能用来做换算。0 是本程序里「查不到 DPI」的约定返回值
        /// （见 <see cref="Common.WindowsAPI.GetWindowDpi"/>），负值和荒谬的大值同样不可用。
        /// </summary>
        public static bool IsUsableDpi(int dpi)
        {
            return dpi > 0 && dpi <= 1000;
        }

        /// <summary>
        /// 把 <paramref name="size"/> 从 <paramref name="fromDpi"/> 换算到 <paramref name="toDpi"/>。
        /// 任一 DPI 不可用时原样返回，由调用方决定退路。
        /// <para>
        /// 给的是显示器之间的比例，而不是逻辑/物理转换：菜单和自绘控件持有的是「某一档 DPI 下的
        /// 像素度量」，需要的正是两档之间的比例。
        /// </para>
        /// </summary>
        public static Size ScaleSize(Size size, int toDpi, int fromDpi)
        {
            if (!IsUsableDpi(toDpi) || !IsUsableDpi(fromDpi) || toDpi == fromDpi)
            {
                return size;
            }

            var factor = (double)toDpi / fromDpi;
            return new Size(Scale(size.Width, factor), Scale(size.Height, factor));
        }

        // There is deliberately no ScaleFont here: fonts are not the application's to rescale by
        // a DPI ratio. There was one, and its only caller was BaseForm.RescaleOwnedFonts — but on
        // a real monitor change the framework has already rescaled every child's Control.Font,
        // including the ones the designer assigned explicitly. It does that from the
        // WM_DPICHANGED_BEFOREPARENT the OS sends to each child window, which a synthetic message
        // cannot reach, so under synthesis it looks like the framework skipped them. What the
        // second multiplication did, with measurements, is on BaseForm.OnDpiContextChanged. A
        // quantity that must follow a font belongs on Control.OnFontChanged, scaled by the ratio
        // of the two font sizes — see StyleItemListBox.HelpFont.

        static int LogicalToPhysicalCoordinate(int value, double scale)
        {
            return Scale(value, scale);
        }

        static int PhysicalToLogicalCoordinate(int value, double scale)
        {
            return Scale(value, scale <= 0d ? 0d : 1d / scale);
        }
    }
}
