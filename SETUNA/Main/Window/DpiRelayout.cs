using System;
using System.Drawing;

namespace SETUNA.Main.Window
{
    /// <summary>
    /// 跨显示器 DPI 变化时的换算规则。这里是重排换算的唯一来源，不引用任何 UI 类型，
    /// 因此可以在没有第二台显示器、也不需要改系统显示设置的环境里直接单测。
    /// <para>
    /// 为什么需要它：DPI 感知由 manifest 声明为 PerMonitorV2，所以窗口被拖过 DPI 边界时，
    /// 系统会在自己的移动循环里替应用把窗口矩形按新旧 DPI 之比改掉；而单文件分发没有应用
    /// 配置文件，WinForms 4.7+ 的 DPI 机制整个是关的（<c>DpiHelper.enableHighDpi</c> 为
    /// false），框架不会重排窗口内容。于是窗口尺寸变了、内容没变，对话框被裁掉一截。重排
    /// 责任落在应用自己身上，入口是 <c>BaseForm</c> 对 <see cref="WM_DPICHANGED"/> 的处理。
    /// </para>
    /// </summary>
    public static class DpiRelayout
    {
        /// <summary>窗口所在显示器的 DPI 变化时系统发来的消息。</summary>
        public const int WM_DPICHANGED = 0x02E0;

        /// <summary>字号下限。<see cref="Font"/> 的构造函数不接受 0 或负数。</summary>
        public const float MinimumPointSize = 1f;

        /// <summary>
        /// 从 <see cref="WM_DPICHANGED"/> 的 <c>wParam</c> 取新 DPI。
        /// 低 16 位是 X 方向、高 16 位是 Y 方向，系统保证两者相同，取低位即可。
        /// </summary>
        public static int DpiFromMessage(IntPtr wParam)
        {
            // 必须先转 Int64 再掩位：64 位下 wParam 的高位可能带符号，
            // 直接转 Int32 会溢出抛异常。
            return (int)(wParam.ToInt64() & 0xFFFF);
        }

        /// <summary>DPI 值是否可用于换算。0 表示「取不到」，不是「96」。</summary>
        public static bool IsUsableDpi(int dpi)
        {
            return dpi > 0;
        }

        /// <summary>是否需要重排。两个 DPI 都要可用，且确实不同。</summary>
        public static bool RequiresRelayout(int newDpi, int oldDpi)
        {
            return IsUsableDpi(newDpi) && IsUsableDpi(oldDpi) && newDpi != oldDpi;
        }

        /// <summary>新旧 DPI 之比。不需要重排时返回 1，调用方因此不必自己防御非法值。</summary>
        public static float Factor(int newDpi, int oldDpi)
        {
            return RequiresRelayout(newDpi, oldDpi) ? (float)newDpi / oldDpi : 1f;
        }

        /// <summary>
        /// 按 DPI 换算字号（磅值）。换窗体字体是重排的触发点：
        /// <c>AutoScaleMode.Font</c> 会据此把子控件坐标、尺寸和窗体客户区一并重排。
        /// <para>
        /// 必须用 double 一次算完，不能先求出 <see cref="Factor"/> 再相乘：字号到像素的换算
        /// 是向上取整的，而常见字号恰好落在整数像素上（宋体 9pt 在 168 DPI 下缩到 96 DPI，
        /// 等效字号 5.142857pt 在 168 DPI 下正好是 12.0 像素）。先乘一个 float 比值会多出
        /// 一个最低位，12.000002 向上取整变成 13 像素，文字随之宽出约 10%——实测同一串文字
        /// 从 131×12 变成 144×13，把相邻控件挤开好几个像素。
        /// </para>
        /// </summary>
        public static float ScalePointSize(float pointSize, int newDpi, int oldDpi)
        {
            if (!RequiresRelayout(newDpi, oldDpi))
            {
                return pointSize < MinimumPointSize ? MinimumPointSize : pointSize;
            }

            var scaled = (float)((double)pointSize * newDpi / oldDpi);

            return scaled < MinimumPointSize ? MinimumPointSize : scaled;
        }

        /// <summary>
        /// 按 DPI 换算字体，保留字族、样式与字符集。
        /// 调用方负责释放原字体——它可能仍被别的控件引用。
        /// </summary>
        public static Font ScaleFont(Font font, int newDpi, int oldDpi)
        {
            // 用 SizeInPoints 而不是 Size：字体可能是以像素为单位定义的，磅值是两者的公共基准。
            return new Font(
                font.FontFamily,
                ScalePointSize(font.SizeInPoints, newDpi, oldDpi),
                font.Style,
                GraphicsUnit.Point,
                font.GdiCharSet,
                font.GdiVerticalFont);
        }

        /// <summary>按 DPI 换算尺寸。</summary>
        public static Size ScaleSize(Size size, int newDpi, int oldDpi)
        {
            return new Size(
                ScaleLength(size.Width, newDpi, oldDpi),
                ScaleLength(size.Height, newDpi, oldDpi));
        }

        /// <summary>
        /// 按框架自己报告的自动缩放尺度之比换算客户区。
        /// <para>
        /// 尺度是环境字体的平均字符尺寸，换字体前后各取一次，比值就是框架给子控件坐标与尺寸
        /// 用的同一个倍率。用它换算客户区，得到的结果与该 DPI 下的原生排版一致，而且往返可逆
        /// （实测 1063×700 ⟷ 580×400 双向精确）。
        /// </para>
        /// <para>
        /// 任一尺度或客户区不可用时返回 <see cref="Size.Empty"/>，调用方据此保持框架给出的
        /// 结果——例如 <c>AutoScaleMode.None</c> 的窗体本来就不该按字体重排。
        /// </para>
        /// </summary>
        public static Size ScaleClientSize(Size client, SizeF before, SizeF after)
        {
            if (client.Width <= 0 || client.Height <= 0
                || before.Width <= 0 || before.Height <= 0
                || after.Width <= 0 || after.Height <= 0)
            {
                return Size.Empty;
            }

            return new Size(
                Round(client.Width * (double)after.Width / before.Width),
                Round(client.Height * (double)after.Height / before.Height));
        }

        /// <summary>
        /// 合成重排后的目标矩形：位置取系统建议矩形，尺寸取重排算出的结果。
        /// <para>
        /// 位置用建议矩形是 <see cref="WM_DPICHANGED"/> 的约定，能让窗口继续留在光标附近。
        /// 尺寸不能用建议矩形的：那是把旧窗口矩形按 DPI 之比线性换算出来的，与该 DPI 下的
        /// 原生排版尺寸差几个像素（实测 611×426 对 586×429），只有重排结果才是对的。
        /// </para>
        /// <para>
        /// 没有建议矩形时（不是由消息触发的重排，例如隐藏期间错过了 DPI 变化、重新显示时
        /// 补一次），保持 <paramref name="fallback"/> 的中心点不动而不是左上角：居中显示的
        /// 对话框因此仍然居中，其他窗口也不会因为尺寸变化被甩向某个角。
        /// </para>
        /// </summary>
        public static Rectangle Compose(Rectangle suggested, Rectangle fallback, Size size)
        {
            if (!suggested.IsEmpty)
            {
                return new Rectangle(suggested.Location, size);
            }

            return new Rectangle(
                fallback.X + (fallback.Width - size.Width) / 2,
                fallback.Y + (fallback.Height - size.Height) / 2,
                size.Width,
                size.Height);
        }

        /// <summary>
        /// 换算单个长度。与字号一样用 double 算，避免先求比值带来的误差。
        /// 0 与负值原样返回，由调用方判断其含义。
        /// </summary>
        public static int ScaleLength(int length, int newDpi, int oldDpi)
        {
            if (length <= 0 || !RequiresRelayout(newDpi, oldDpi))
            {
                return length;
            }

            return Round(length * (double)newDpi / oldDpi);
        }

        /// <summary>
        /// 四舍五入到不小于 1 的整数像素。四舍五入而不是截断：截断会让每次跨越 DPI 边界都少一个
        /// 像素，往返之后回不到原尺寸（746 → 426 → 745.5，截断成 745，四舍五入回到 746）。
        /// </summary>
        static int Round(double value)
        {
            var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);

            return rounded < 1 ? 1 : rounded;
        }
    }
}
