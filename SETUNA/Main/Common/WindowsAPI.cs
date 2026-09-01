
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

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
        private static extern int GetWindowModuleFileName(IntPtr hWnd, StringBuilder lpFilename, int nSize);

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

        public static string GetModuleName(IntPtr hwnd)
        {
            var builder = new StringBuilder(1024);
            var len = GetWindowModuleFileName(hwnd, builder, builder.Capacity);

            return builder.ToString();
        }

        public static string GetClassName(IntPtr hwnd)
        {
            var builder = new StringBuilder(1024);
            var len = GetClassName(hwnd, builder, builder.Capacity);

            return builder.ToString();
        }


        public const int GW_HWNDNEXT = 2;
        public const int GW_HWNDPREV = 3;

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
        /// 不能用 <c>Control.DeviceDpi</c>：DPI 感知由 manifest 声明而没有应用配置文件，
        /// WinForms 的 DPI 机制整个是关的（<c>DpiHelper.enableHighDpi</c> 为 false），
        /// 此时 <c>Control.DeviceDpi</c> 返回的是框架的逻辑常量 96，与窗口实际所在的显示器
        /// 无关——实测系统 DPI 为 168 时它仍然是 96，把它传给按 DPI 的换算会得到 100%
        /// 缩放下的值。
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

        /// <summary>系统 DPI（主显示器的缩放比例）。Windows 10 1607 起可用。</summary>
        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        /// <summary>
        /// 系统 DPI。WinForms 给窗体排版用的就是这个值：环境字体的像素高度由它决定，
        /// <c>AutoScaleMode.Font</c> 的倍率又由环境字体决定，所以窗体建好时的排版对应的是
        /// 系统 DPI，而不是窗口实际所在显示器的 DPI。跨显示器重排需要这个基准值。
        /// </summary>
        public static int GetSystemDpi()
        {
            return (int)GetDpiForSystem();
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

        /// <summary>显示器的 DPI。Windows 8.1 起可用。</summary>
        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>
        /// 取包含 <paramref name="point"/> 那块显示器的 DPI，取不到时返回 0
        /// （表示「不要据此换算」，与 <see cref="GetWindowDpi"/> 一致）。
        /// <para>
        /// 弹出式界面需要它：右键菜单、托盘菜单的窗口在将要弹出的那一刻还没建好，拿不到句柄，
        /// <see cref="GetWindowDpi"/> 因此用不上，只能按它将要出现的位置去问显示器。
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

        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>按指定 DPI 把客户区矩形放大为窗口矩形。Windows 10 1607 起可用。</summary>
        [DllImport("user32.dll")]
        static extern bool AdjustWindowRectExForDpi(ref NativeRect rect, int dwStyle, bool bMenu, int dwExStyle, uint dpi);

        /// <summary>
        /// 求 <paramref name="clientSize"/> 这个客户区在 <paramref name="dpi"/> 下对应的窗口外框
        /// 尺寸；参数不可用或调用失败时返回 <see cref="Size.Empty"/>，由调用方决定退路。
        /// <para>
        /// 跨显示器重排需要它，而不能用 <c>Form.ClientSize</c> 的 setter：那条路走
        /// <c>Form.SizeFromClientSize</c>，里面调的是不带 DPI 参数的 <c>AdjustWindowRectEx</c>，
        /// 用的是系统 DPI 的非客户区厚度。实测系统 DPI 为 168、窗口已经跨到 96 DPI 显示器上时，
        /// 它按 46 像素的标题栏算外框，而实际只有 29，多出来的 17 像素全落到客户区上——客户区
        /// 成了 417 而不是原生排版的 400，对话框底部空出一条。这个函数按窗口实际所处显示器的
        /// DPI 反算，两个方向都与原生排版一致。
        /// </para>
        /// <para>
        /// <c>bMenu</c> 传 <c>false</c>：本程序的窗体没有传统菜单栏，<c>MenuStrip</c> 一类是
        /// 客户区内的普通控件，不占非客户区。
        /// </para>
        /// </summary>
        public static Size GetOuterSizeForClientSize(IntPtr hwnd, Size clientSize, int dpi)
        {
            if (hwnd == IntPtr.Zero || dpi <= 0 || clientSize.Width <= 0 || clientSize.Height <= 0)
            {
                return Size.Empty;
            }

            var rect = new NativeRect
            {
                Left = 0,
                Top = 0,
                Right = clientSize.Width,
                Bottom = clientSize.Height
            };

            if (!AdjustWindowRectExForDpi(
                ref rect,
                GetWindowLong(hwnd, GWL_STYLE),
                false,
                GetWindowLong(hwnd, GWL_EXSTYLE),
                (uint)dpi))
            {
                return Size.Empty;
            }

            return new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
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
