
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using SETUNA.Main.Window;

namespace SETUNA.Main
{
    public class WindowsAPI
    {
        // Token: 0x06000459 RID: 1113
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int SendMessage(System.IntPtr h, int m, System.IntPtr w, System.IntPtr l);

        // Token: 0x0600045A RID: 1114
        [DllImport("comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool InitCommonControls();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpFilename, int nSize);

        public static bool GetWindowZOrder(IntPtr hwnd, out int zOrder)
        {
            const uint GW_HWNDPREV = 3;
            const uint GW_HWNDLAST = 1;

            var lowestHwnd = GetWindow(hwnd, GW_HWNDLAST);

            var z = 0;
            var hwndTmp = lowestHwnd;
            while (hwndTmp != IntPtr.Zero)
            {
                if (hwnd == hwndTmp)
                {
                    zOrder = z;
                    return true;
                }

                hwndTmp = GetWindow(hwndTmp, GW_HWNDPREV);
                z++;
            }

            zOrder = int.MinValue;
            return false;
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            var length = GetWindowTextLength(hwnd);
            if (length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(length);
            GetWindowText(hwnd, builder, length + 1);

            return builder.ToString();
        }

        public static string GetClassName(IntPtr hwnd)
        {
            var builder = new StringBuilder(1024);
            var len = GetClassName(hwnd, builder, builder.Capacity);

            return builder.ToString();
        }


        public const int GW_HWNDNEXT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "GetWindow", SetLastError = true)]
        public static extern IntPtr GetNextWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.U4)] int wFlag);

        /// <summary>
        /// 返回系统最顶层的可见窗口，链表走完仍未找到时返回 <see cref="IntPtr.Zero"/>。
        /// </summary>
        public static IntPtr GetTopMostWindow()
        {
            return FindFirstVisible(
                GetTopWindow(IntPtr.Zero),
                hwnd => GetNextWindow(hwnd, GW_HWNDNEXT),
                IsWindowVisible);
        }

        /// <summary>
        /// 沿窗口链表向后查找第一个可见窗口。遍历在句柄为空时终止。
        /// 抽成不依赖 Win32 的形式，使终止性可以直接验证。
        /// </summary>
        public static IntPtr FindFirstVisible(IntPtr start, Func<IntPtr, IntPtr> getNext, Func<IntPtr, bool> isVisible)
        {
            var hwnd = start;

            // 必须判断句柄非空：GetNextWindow 在链表末端返回 IntPtr.Zero，
            // 而 IsWindowVisible(IntPtr.Zero) 恒为 false，
            // 原来的 while (!IsWindowVisible(hwnd)) 会就此死循环。
            while (hwnd != IntPtr.Zero && !isVisible(hwnd))
            {
                hwnd = getNext(hwnd);
            }

            return hwnd;
        }


        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref Rect rectangle);


        /// <summary>
        /// 窗口所在显示器的 DPI。Windows 10 1607 起可用，覆盖 manifest 里声明的支持范围。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        /// <summary>
        /// 取窗口所在显示器的 DPI，取不到时返回 0（表示「不要据此换算」）。
        /// 这是本程序里 DPI 值的唯一来源。
        /// <para>
        /// net8 的 WinForms 高 DPI 管线是开着的，<c>Control.DeviceDpi</c> 确实会报告真实
        /// 窗口 DPI（实测 168），所以这里不再是「绕开一个坏掉的属性」。仍然用这一条的理由是
        /// 它对任何 HWND 都成立：托盘菜单弹出前、以及别的进程的窗口都没有 <c>Control</c>，
        /// 而显示器快照必须由同一个 DPI 来源拼出来，否则同一次换算里会混进两个值。
        /// </para>
        /// <para>
        /// 这是查询单个窗口的 DPI，不设置进程或线程的感知级别，因此与「感知级别只由
        /// manifest 声明」这条约束不冲突。
        /// </para>
        /// </summary>
        public static int GetWindowDpi(IntPtr hwnd)
        {
            return hwnd == IntPtr.Zero ? 0 : (int)GetDpiForWindow(hwnd);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint
        {
            public int X;
            public int Y;
        }

        /// <summary>取包含指定点的显示器句柄。</summary>
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MonitorInfoEx
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr clip,
            MonitorEnumProc callback,
            IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        /// <summary>显示器的 DPI。Windows 8.1 起可用。</summary>
        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>
        /// 取包含 <paramref name="point"/> 那块显示器的 DPI，取不到时返回 0
        /// （表示「不要据此换算」，与 <see cref="GetWindowDpi"/> 一致）。
        /// <para>
        /// 还没有窗口句柄的东西只能这样问：<see cref="GetWindowDpi"/> 要 HWND，而弹出式界面、
        /// 截图框这类几何在确定位置的那一刻还没有窗口。菜单曾经是这里的主要调用方，net8 的
        /// <c>ToolStrip</c> DPI 管线接手之后不再需要（见 <c>ContextStyleMenuStrip</c> 与
        /// <c>probes/MenuDpiProbe</c>），但按点取 DPI 这件事本身还在，<c>MenuDpiTests</c>
        /// 钉住每块显示器都答得出一个可用值。
        /// </para>
        /// </summary>
        public static int GetMonitorDpiAt(Point point)
        {
            const uint MONITOR_DEFAULTTONEAREST = 2;
            const int MDT_EFFECTIVE_DPI = 0;

            var monitor = MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return 0;
            }

            uint dpiX;
            uint dpiY;
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
            {
                return 0;
            }

            // X 与 Y 方向系统保证相同，取一个即可。
            return (int)dpiX;
        }

        /// <summary>
        /// Returns the complete physical monitor snapshot containing <paramref name="point"/>.
        /// This is the query to use before a popup window has a handle.
        /// </summary>
        public static MonitorSnapshot GetMonitorSnapshotAt(Point point)
        {
            const uint MONITOR_DEFAULTTONEAREST = 2;
            return GetMonitorSnapshot(MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST));
        }

        /// <summary>
        /// 一个矩形归哪块显示器：**重叠面积最大**的那块，而不是最近的那块。判据与理由见
        /// <see cref="MonitorSnapshot.SelectFor"/>；这里只负责枚举当前的显示器。
        /// <para>
        /// 矩形与任何显示器都不相交（例如完全落在显示器之间的空隙里）时退回按矩形中心取，
        /// 中心也查不到就报不可用。
        /// </para>
        /// </summary>
        public static MonitorSnapshot GetMonitorSnapshotFor(Rectangle rectangle)
        {
            var best = MonitorSnapshot.SelectFor(rectangle, EnumerateMonitorSnapshots());
            if (best.IsAvailable)
            {
                return best;
            }

            return GetMonitorSnapshotAt(new Point(
                rectangle.Left + rectangle.Width / 2,
                rectangle.Top + rectangle.Height / 2));
        }

        /// <summary>
        /// Returns the complete physical monitor snapshot for an existing window. Its DPI comes
        /// from GetDpiForWindow rather than Control.DeviceDpi.
        /// <para>
        /// 这里不需要 <see cref="MonitorSnapshot.SelectFor"/> 那条「重叠最多」的规则：
        /// <c>MonitorFromWindow</c> 配 <c>MONITOR_DEFAULTTONEAREST</c> 的定义就是「与窗口外框
        /// 相交面积最大的那块显示器」，系统已经按面积算过了。那条规则是给没有窗口的矩形用的。
        /// </para>
        /// </summary>
        public static MonitorSnapshot GetMonitorSnapshotForWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return MonitorSnapshot.Unavailable;
            }

            var monitor = MonitorFromWindow(hwnd, 2);
            var snapshot = GetMonitorSnapshot(monitor);
            var dpi = GetWindowDpi(hwnd);
            if (dpi <= 0 || !snapshot.IsAvailable)
            {
                return snapshot;
            }

            return new MonitorSnapshot(
                snapshot.Handle,
                snapshot.DeviceName,
                snapshot.NativeBounds,
                snapshot.WorkingArea,
                dpi,
                dpi,
                snapshot.IsPrimary);
        }

        /// <summary>Enumerates all active monitors without converting their negative origins.</summary>
        public static IReadOnlyList<MonitorSnapshot> EnumerateMonitorSnapshots()
        {
            var snapshots = new List<MonitorSnapshot>();
            MonitorEnumProc callback = (monitor, hdc, rect, data) =>
            {
                var snapshot = GetMonitorSnapshot(monitor);
                // Keep unavailable entries visible to callers. An empty list would
                // make a DPI query failure indistinguishable from "no monitors".
                snapshots.Add(snapshot);

                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            {
                return snapshots;
            }

            return snapshots;
        }

        static MonitorSnapshot GetMonitorSnapshot(IntPtr monitor)
        {
            if (monitor == IntPtr.Zero)
            {
                return MonitorSnapshot.Unavailable;
            }

            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf(typeof(MonitorInfoEx)) };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return MonitorSnapshot.Unavailable;
            }

            uint dpiX;
            uint dpiY;
            const int MDT_EFFECTIVE_DPI = 0;
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0 || dpiX == 0 || dpiY == 0)
            {
                return MonitorSnapshot.Unavailable;
            }

            return new MonitorSnapshot(
                monitor,
                info.szDevice,
                ToRectangle(info.rcMonitor),
                ToRectangle(info.rcWork),
                (int)dpiX,
                (int)dpiY,
                (info.dwFlags & 1u) != 0);
        }

        static Rectangle ToRectangle(NativeRect rect)
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }



        private const int CURSOR_SHOWING = 0x00000001;
        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(out CURSORINFO pci);
        [DllImport("user32.dll")]
        static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

        /// <summary>
        /// 将鼠标指针形状绘制到屏幕截图上
        /// </summary>
        /// <param name="g"></param>
        public static void DrawCursorImageToScreenImage(Point position, IntPtr hDC)
        {
            CURSORINFO vCurosrInfo;
            vCurosrInfo.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
            GetCursorInfo(out vCurosrInfo);
            if (vCurosrInfo.flags == CURSOR_SHOWING)
            {
                DrawIcon(hDC, position.X, position.Y, vCurosrInfo.hCursor);
            }
        }
    }

    public struct Rect
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
    }


    [StructLayout(LayoutKind.Sequential)]
    struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public Point ptScreenPos;
    }
}
