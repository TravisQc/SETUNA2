using System.Drawing;

namespace SETUNA.Main.Window
{
    /// <summary>
    /// 主窗口的尺寸基线与边界计算。这里是运行时尺寸约束的唯一来源，
    /// 设计器中的同名字面量只用于 Visual Studio 的设计时预览。
    /// <para>
    /// 最小与最大尺寸都是在 175%（168 DPI）下实测得到的基线值，按当前显示器的 DPI 换算，
    /// 因此在每台显示器上描述的是同一个物理尺寸。换算用截断而不是四舍五入：边界的作用是
    /// 划定允许区间，宁可略微向区间内侧收，也不要因为进位而挡住基线 DPI 下本来能达到的
    /// 尺寸。窗口尺寸本身的换算是另一回事，见 <see cref="DpiRelayout.ScaleSize"/>。
    /// </para>
    /// </summary>
    public static class MainWindowGeometry
    {
        /// <summary>首次显示时的客户区尺寸。</summary>
        public const int DefaultClientWidth = 415;
        public const int DefaultClientHeight = 180;

        /// <summary>
        /// 最大外框尺寸的基线，与最小尺寸同在 175%（168 DPI）下测得。
        /// <para>
        /// 曾经是不随 DPI 换算的固定像素值。那与「窗体随显示器 DPI 重排」不相容：窗口移到
        /// 高 DPI 显示器时排版会按比例放大，而上限不动，窗口就被钳在旧尺寸上并裁掉内容。
        /// </para>
        /// </summary>
        public const int MaximumBaselineWidth = 640;
        public const int MaximumBaselineHeight = 360;

        /// <summary>
        /// 仍能完整显示两个操作标签的最小外框尺寸，在 175% 缩放（168 DPI）下实测得到。
        /// <see cref="BaselineDpi"/> 记录测量时的 DPI，以便在其他显示器上还原同一物理尺寸。
        /// </summary>
        public const int MinimumBaselineWidth = 260;
        public const int MinimumBaselineHeight = 160;

        /// <summary>两个基线尺寸的测量 DPI。</summary>
        public const int BaselineDpi = 168;

        /// <summary>
        /// 按 <paramref name="dpi"/> 换算最小外框尺寸。
        /// <paramref name="dpi"/> 非正时返回 <see cref="Size.Empty"/>，表示不应改动当前的最小尺寸。
        /// </summary>
        public static Size ScaleMinimum(int dpi)
        {
            return Scale(MinimumBaselineWidth, MinimumBaselineHeight, dpi);
        }

        /// <summary>
        /// 按 <paramref name="dpi"/> 换算最大外框尺寸。
        /// <paramref name="dpi"/> 非正时返回 <see cref="Size.Empty"/>，表示不应改动当前的最大尺寸。
        /// </summary>
        public static Size ScaleMaximum(int dpi)
        {
            return Scale(MaximumBaselineWidth, MaximumBaselineHeight, dpi);
        }

        static Size Scale(int baselineWidth, int baselineHeight, int dpi)
        {
            if (dpi <= 0)
            {
                return Size.Empty;
            }

            return new Size(
                baselineWidth * dpi / BaselineDpi,
                baselineHeight * dpi / BaselineDpi);
        }

        /// <summary>首次显示时的客户区尺寸。</summary>
        public static Size DefaultClientSize => new Size(DefaultClientWidth, DefaultClientHeight);

        /// <summary>把外框尺寸钳制到 [<paramref name="minimum"/>, <paramref name="maximum"/>] 区间内。</summary>
        public static Size Clamp(int width, int height, Size minimum, Size maximum)
        {
            return new Size(
                Clamp(width, minimum.Width, maximum.Width),
                Clamp(height, minimum.Height, maximum.Height));
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
