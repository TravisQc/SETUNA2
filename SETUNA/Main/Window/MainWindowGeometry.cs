using System.Drawing;

namespace SETUNA.Main.Window
{
    /// <summary>
    /// 主窗口的尺寸基线、边界与持久化换算。这里是运行时尺寸约束的唯一来源，
    /// 设计器中的同名字面量只用于 Visual Studio 的设计时预览。
    /// <para>
    /// **四个基线都是 96 DPI 的逻辑值**，与设计器的
    /// <c>AutoScaleDimensions = (96F, 96F)</c> 同一套单位。它们由 175%（168 DPI）下的实测值
    /// 折算而来——`AutoScaleMode.None` 时代量到的默认客户区 415x180、最小外框 260x160、
    /// 最大外框 640x360 全是那一档的物理像素——所以按 168 DPI 换算回去会逐项还原成原来
    /// 的数字。把它们留在 168 DPI 上是与「窗体随显示器 DPI 重排」不相容的：设计器基线一旦
    /// 是 96 DPI，同一个字面量就会被框架再乘一次倍率，实测 168 DPI 下客户区变成 726x315
    /// 而不是 415x180。
    /// </para>
    /// <para>
    /// 换算走 <see cref="DpiContext"/>，因此与本项目其他所有逻辑/物理转换同一套舍入规则
    /// （<c>AwayFromZero</c>，一次乘法）。窗口尺寸本身也是物理像素——WinForms 的
    /// <c>Size</c>／<c>MinimumSize</c>／<c>MaximumSize</c> 都直接落到 Win32 窗口矩形上，
    /// 没有运行时的逻辑坐标系，见 <see cref="RestoreWindowSize"/>。
    /// </para>
    /// </summary>
    public static class MainWindowGeometry
    {
        /// <summary>四个基线所在的 DPI 档：与设计器一致的 96。</summary>
        public const int BaselineDpi = DpiContext.BaseDpi;

        /// <summary>「保存值没有记录 DPI」的约定值，用于迁移本变更之前写下的配置。</summary>
        public const int UnknownDpi = 0;

        /// <summary>首次显示时的客户区尺寸（96 DPI 逻辑值，168 DPI 下还原为 415x180）。</summary>
        public const int DefaultClientWidth = 237;
        public const int DefaultClientHeight = 103;

        /// <summary>最大外框尺寸（96 DPI 逻辑值，168 DPI 下还原为 641x361）。</summary>
        public const int MaximumBaselineWidth = 366;
        public const int MaximumBaselineHeight = 206;

        /// <summary>
        /// 仍能完整显示两个操作标签的最小外框尺寸（96 DPI 逻辑值，168 DPI 下还原为
        /// 261x159，即实测值 260x160 的一个像素之内）。
        /// </summary>
        public const int MinimumBaselineWidth = 149;
        public const int MinimumBaselineHeight = 91;

        /// <summary>
        /// 按 <paramref name="dpi"/> 换算最小外框尺寸。
        /// <paramref name="dpi"/> 不可用时返回 <see cref="Size.Empty"/>，表示不应改动当前的最小尺寸。
        /// </summary>
        public static Size ScaleMinimum(int dpi)
        {
            return ScaleFromBaseline(MinimumBaselineWidth, MinimumBaselineHeight, dpi);
        }

        /// <summary>
        /// 按 <paramref name="dpi"/> 换算最大外框尺寸。
        /// <paramref name="dpi"/> 不可用时返回 <see cref="Size.Empty"/>，表示不应改动当前的最大尺寸。
        /// </summary>
        public static Size ScaleMaximum(int dpi)
        {
            return ScaleFromBaseline(MaximumBaselineWidth, MaximumBaselineHeight, dpi);
        }

        /// <summary>
        /// 按 <paramref name="dpi"/> 换算首次显示的客户区尺寸。没有保存值时施加的就是这一个，
        /// 直接把逻辑基线当像素用会让 175% 下的窗口只有设计尺寸的 57%。
        /// </summary>
        public static Size ScaleDefaultClient(int dpi)
        {
            return ScaleFromBaseline(DefaultClientWidth, DefaultClientHeight, dpi);
        }

        static Size ScaleFromBaseline(int baselineWidth, int baselineHeight, int dpi)
        {
            // DpiContext.Scale 对不可用的倍率原样返回，会把「查不到 DPI」伪装成
            // 「基线就是答案」，所以先在这里挡掉。
            if (!DpiContext.IsUsableDpi(dpi))
            {
                return Size.Empty;
            }

            return DpiContext.FromDpi(dpi, dpi).LogicalToPhysical(new Size(baselineWidth, baselineHeight));
        }

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

        /// <summary>
        /// 把保存下来的物理尺寸还原成当前显示器上应当施加的窗口外框尺寸。
        /// 没有可用的保存值时返回 <see cref="Size.Empty"/>，由调用方施加默认客户区。
        /// <para>
        /// **这是持久化物理像素与窗口物理像素之间唯一的换算点。** 保存的字段是物理像素，
        /// 而 WinForms 的 <c>Size</c> 也是物理像素，所以「同一档 DPI 上」这一步是恒等的；
        /// 真正的换算发生在保存时的显示器与现在的显示器不是同一档时——
        /// <paramref name="persistedDpi"/> 记录了前者，于是保存值可以按两档之比一次换算，
        /// 用户看到的窗口大小在两块显示器上是同一个视觉尺寸。缺了这个字段就只能按当前显示器
        /// 解释一次（下面的旧配置分支），而那会让「在高 DPI 屏上关闭、回到低 DPI 屏上启动」
        /// 每次都把窗口顶到上限。
        /// </para>
        /// <para>
        /// <paramref name="diagnostic"/> 在保存值被拒绝、按旧配置解释或被边界钳制时给出说明，
        /// 否则为 <see langword="null"/>。调用方负责输出：无法判定 DPI 的旧值必须留下痕迹，
        /// 否则「窗口大小自己变了」就无从解释。
        /// </para>
        /// </summary>
        public static Size RestoreWindowSize(
            int persistedWidth,
            int persistedHeight,
            int persistedDpi,
            int currentDpi,
            Size minimum,
            Size maximum,
            out string diagnostic)
        {
            diagnostic = null;

            if (!HasPersistedSize(persistedWidth, persistedHeight))
            {
                // 全 0 是「还没保存过」，不值得报告；其余组合（负数、只有一维）是坏值。
                if (persistedWidth != 0 || persistedHeight != 0)
                {
                    diagnostic = "Saved main window size " + persistedWidth + "x" + persistedHeight
                        + " is not a usable size; opening at the default.";
                }

                return Size.Empty;
            }

            var saved = new Size(persistedWidth, persistedHeight);
            var restored = saved;

            if (DpiContext.IsUsableDpi(persistedDpi) && DpiContext.IsUsableDpi(currentDpi))
            {
                // 一次乘法，不经过逻辑单位往返：两次舍入会在反复跨屏时累积。
                restored = DpiContext.ScaleSize(saved, currentDpi, persistedDpi);
            }
            else if (!DpiContext.IsUsableDpi(persistedDpi))
            {
                diagnostic = "Saved main window size " + saved.Width + "x" + saved.Height
                    + " carries no monitor DPI, so it was written before this field existed;"
                    + " interpreting it once as pixels on the current monitor.";
            }

            var clamped = Clamp(restored.Width, restored.Height, minimum, maximum);
            if (clamped != restored)
            {
                diagnostic = Append(
                    diagnostic,
                    "Restored main window size " + restored.Width + "x" + restored.Height
                        + " is outside the current monitor's bounds " + Describe(minimum) + ".."
                        + Describe(maximum) + "; clamped to " + Describe(clamped) + ".");
            }

            return clamped;
        }

        /// <summary>
        /// 与窗口尺寸一起保存的 DPI。不可用时保存 <see cref="UnknownDpi"/>，读回时走
        /// <see cref="RestoreWindowSize"/> 的旧配置分支，而不是拿一个假的 96 去换算。
        /// </summary>
        public static int PersistableDpi(int dpi)
        {
            return DpiContext.IsUsableDpi(dpi) ? dpi : UnknownDpi;
        }

        static string Append(string diagnostic, string sentence)
        {
            return string.IsNullOrEmpty(diagnostic) ? sentence : diagnostic + " " + sentence;
        }

        static string Describe(Size size)
        {
            return size.Width + "x" + size.Height;
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
