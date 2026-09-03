using System;
using System.Collections.Generic;
using System.Drawing;

namespace SETUNA.Main.Window
{
    /// <summary>
    /// Immutable description of one display in the physical virtual-screen coordinate space.
    /// An unavailable snapshot is explicit; callers must not silently reinterpret it as 96 DPI.
    /// </summary>
    public sealed class MonitorSnapshot
    {
        public MonitorSnapshot(
            IntPtr handle,
            string deviceName,
            Rectangle nativeBounds,
            Rectangle workingArea,
            int dpiX,
            int dpiY,
            bool isPrimary,
            bool isAvailable = true)
        {
            Handle = handle;
            DeviceName = deviceName ?? string.Empty;
            NativeBounds = nativeBounds;
            WorkingArea = workingArea;
            DpiX = dpiX;
            DpiY = dpiY;
            IsPrimary = isPrimary;
            IsAvailable = isAvailable && dpiX > 0 && dpiY > 0;
            ScaleX = IsAvailable ? dpiX / 96d : 0d;
            ScaleY = IsAvailable ? dpiY / 96d : 0d;
        }

        public IntPtr Handle { get; }

        public string DeviceName { get; }

        /// <summary>Full monitor bounds in physical pixels; may have a negative origin.</summary>
        public Rectangle NativeBounds { get; }

        /// <summary>Monitor work area in physical pixels; may have a negative origin.</summary>
        public Rectangle WorkingArea { get; }

        public int DpiX { get; }

        public int DpiY { get; }

        public double ScaleX { get; }

        public double ScaleY { get; }

        public bool IsPrimary { get; }

        public bool IsAvailable { get; }

        public static MonitorSnapshot Unavailable { get; } = new MonitorSnapshot(
            IntPtr.Zero,
            string.Empty,
            Rectangle.Empty,
            Rectangle.Empty,
            0,
            0,
            false,
            false);

        /// <summary>
        /// 从 <paramref name="candidates"/> 里挑出 <paramref name="rectangle"/> **重叠面积最大**
        /// 的那块显示器，都不相交时返回 <see cref="Unavailable"/>。
        /// <para>
        /// 截图框和贴图窗口可以横跨两块显示器，而缩放倍率、捕获尺寸都要按「归哪块」来算。
        /// 「最近」（<c>MONITOR_DEFAULTTONEAREST</c>，也等于「第一块相交的」）给出的答案取决
        /// 于枚举顺序；「重叠最多」是确定的。ScreenToGif 为同一件事也手工算重叠，它源码里的
        /// 理由是 <c>Rect.Intersect</c> *"does not work properly with multi DPI"*——这里全程在
        /// <see cref="NativeBounds"/> 的物理像素上算，不经过任何 DPI 换算，所以那个理由在本
        /// 实现里自动成立。
        /// </para>
        /// <para>
        /// 平局偏向主显示器：正好一半一半时选主屏是可预期的。ScreenToGif 那份实现用
        /// <c>ThenBy(IsPrimary)</c>，而 <c>false</c> 排在 <c>true</c> 之前，平局时选的是副屏。
        /// 不可用的候选一概跳过，宁可返回不可用也不拿一块没有 DPI 的显示器去换算。
        /// </para>
        /// </summary>
        public static MonitorSnapshot SelectFor(Rectangle rectangle, IEnumerable<MonitorSnapshot> candidates)
        {
            if (candidates == null)
            {
                return Unavailable;
            }

            var best = Unavailable;
            var bestOverlap = 0L;

            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.IsAvailable)
                {
                    continue;
                }

                var overlap = OverlapArea(rectangle, candidate.NativeBounds);
                if (overlap <= 0)
                {
                    continue;
                }

                if (overlap > bestOverlap || (overlap == bestOverlap && candidate.IsPrimary))
                {
                    best = candidate;
                    bestOverlap = overlap;
                }
            }

            return best;
        }

        /// <summary>
        /// 两个物理像素矩形的重叠面积。用 <see cref="long"/>：4K 级别的宽高相乘已经接近
        /// <see cref="int"/> 上限，而虚拟桌面可以比单块显示器大得多。
        /// </summary>
        static long OverlapArea(Rectangle a, Rectangle b)
        {
            var width = (long)Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            var height = (long)Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

            return width <= 0 || height <= 0 ? 0L : width * height;
        }
    }
}
