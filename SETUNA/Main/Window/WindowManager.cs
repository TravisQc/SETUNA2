
using System;
using System.Drawing;

namespace SETUNA.Main
{
    internal class WindowManager
    {
        public delegate void WindowHandler(object sender, WindowInfo windowInfo);

        public static event WindowHandler WindowActived;
        public static event WindowHandler TopMostChanged;


        public static readonly WindowManager Instance = new WindowManager();

        public WindowInfo CurrentForegroundWindow => foregroundWindow;
        public WindowInfo TopMostWindow => topMostWindow;

        WindowInfo foregroundWindow;
        WindowInfo topMostWindow;


        public void Update()
        {
            // 事件只在对应窗口真正变化时触发。以前两个 Invoke 在 if 之外，
            // 于是每个定时器周期都会触发一次 CheckRefreshLayer，
            // 而后者会为每个已跟踪窗体各做一次全局窗口枚举。
            var hwnd = WindowsAPI.GetForegroundWindow();
            if (foregroundWindow.Handle != hwnd)
            {
                foregroundWindow = GetWindowInfo(hwnd);
                WindowActived?.Invoke(this, foregroundWindow);
            }

            hwnd = WindowsAPI.GetTopMostWindow();
            if (topMostWindow.Handle != hwnd)
            {
                topMostWindow = GetWindowInfo(hwnd);
                TopMostChanged?.Invoke(this, topMostWindow);
            }
        }

        /// <summary>
        /// 取窗口信息。<paramref name="includeZOrder"/> 为 false 时跳过 Z 序获取——
        /// 那是一次全局顶层窗口枚举，而多数调用方只需要 Rect。
        /// </summary>
        public WindowInfo GetWindowInfo(IntPtr hwnd, bool includeZOrder = true)
        {
            var titleName = WindowsAPI.GetWindowTitle(hwnd);
            var className = WindowsAPI.GetClassName(hwnd);

            var zOrder = 0;
            if (includeZOrder)
            {
                WindowsAPI.GetWindowZOrder(hwnd, out zOrder);
            }

            var rect = new Rect();
            WindowsAPI.GetWindowRect(hwnd, ref rect);

            return new WindowInfo()
            {
                Handle = hwnd,
                TitleName = titleName,
                ClassName = className,
                Rect = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                ZOrder = zOrder,
            };
        }
    }

    public struct WindowInfo
    {
        public static WindowInfo Empty { get; internal set; }


        public IntPtr Handle { set; get; }
        public string TitleName { set; get; }
        public string ClassName { set; get; }
        public int ZOrder { set; get; }
        public Rectangle Rect { set; get; }


        public override string ToString()
        {
            return string.Format(
                $"{nameof(Handle)}:{Handle}," +
                $"{nameof(TitleName)}:{TitleName}," +
                $"{nameof(ClassName)}:{ClassName}," +
                $"{nameof(Rect)}:(X:{Rect.X},Y:{Rect.Y},W:{Rect.Width},H:{Rect.Height})," +
                $"{nameof(ZOrder)}:{ZOrder}");
        }

        public override int GetHashCode()
        {
            return ~(int)Handle;
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public static bool operator ==(WindowInfo lhs, WindowInfo rhs)
        {
            return lhs.Handle == rhs.Handle;
        }
        public static bool operator !=(WindowInfo lhs, WindowInfo rhs)
        {
            return lhs.Handle != rhs.Handle;
        }
    }
}
