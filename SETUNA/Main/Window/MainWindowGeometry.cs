using System.Drawing;

namespace SETUNA.Main.Window
{
    /// <summary>
    /// 主窗口的尺寸基线与边界计算。这里是运行时尺寸约束的唯一来源，
    /// 设计器中的同名字面量只用于 Visual Studio 的设计时预览。
    /// </summary>
    public static class MainWindowGeometry
    {
        /// <summary>首次显示时的客户区尺寸。</summary>
        public const int DefaultClientWidth = 415;
        public const int DefaultClientHeight = 180;

        /// <summary>最大外框尺寸。固定像素值，不随显示器 DPI 换算。</summary>
        public const int MaximumWidth = 640;
        public const int MaximumHeight = 360;

        /// <summary>
        /// 仍能完整显示两个操作标签的最小外框尺寸，在 175% 缩放（168 DPI）下实测得到。
        /// <see cref="MinimumBaselineDpi"/> 记录测量时的 DPI，以便在其他显示器上还原同一物理尺寸。
        /// </summary>
        public const int MinimumBaselineWidth = 260;
        public const int MinimumBaselineHeight = 160;
        public const int MinimumBaselineDpi = 168;

        /// <summary>
        /// 按 <paramref name="dpi"/> 换算最小外框尺寸。
        /// <paramref name="dpi"/> 非正时返回 <see cref="Size.Empty"/>，表示不应改动当前的最小尺寸。
        /// </summary>
        public static Size ScaleMinimum(int dpi)
        {
            if (dpi <= 0)
            {
                return Size.Empty;
            }

            return new Size(
                MinimumBaselineWidth * dpi / MinimumBaselineDpi,
                MinimumBaselineHeight * dpi / MinimumBaselineDpi);
        }

        /// <summary>固定的最大外框尺寸。</summary>
        public static Size Maximum => new Size(MaximumWidth, MaximumHeight);

        /// <summary>首次显示时的客户区尺寸。</summary>
        public static Size DefaultClientSize => new Size(DefaultClientWidth, DefaultClientHeight);

        /// <summary>把外框尺寸钳制到 [<paramref name="minimum"/>, <see cref="Maximum"/>] 区间内。</summary>
        public static Size Clamp(int width, int height, Size minimum)
        {
            return new Size(
                Clamp(width, minimum.Width, MaximumWidth),
                Clamp(height, minimum.Height, MaximumHeight));
        }

        /// <summary>判断保存下来的尺寸是否有效。0 或负值表示「没有有效的保存值」，应回退到默认尺寸。</summary>
        public static bool HasPersistedSize(int width, int height)
        {
            return width > 0 && height > 0;
        }

        static int Clamp(int value, int minimum, int maximum)
        {
            // 先取 Min 再取 Max：minimum 大于 maximum 时以 minimum 为准，
            // 保证返回值永远不小于调用方要求的下界。
            if (value > maximum)
            {
                value = maximum;
            }

            return value < minimum ? minimum : value;
        }
    }
}
