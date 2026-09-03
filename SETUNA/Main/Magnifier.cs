using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using SETUNA.Main.Window;

namespace SETUNA.Main
{
    public partial class Magnifier : BaseForm
    {
        protected override DpiPolicy DpiPolicy => DpiPolicy.PhysicalSurface;
        /// <summary>
        /// 兜底刷新间隔。主路径是截图窗口的鼠标移动事件，这个定时器只负责补上不产生
        /// MouseMove 的光标移动（键盘移动光标、光标离开截图窗口后又回来）。
        /// </summary>
        const int FallbackIntervalMs = 100;

        static readonly Point NoCursor = new Point(int.MinValue, int.MinValue);

        readonly Timer timer;

        Bitmap buffer;
        Graphics bufferGraphics;

        /// <summary>上一次真正重绘时的光标位置。光标没动就整体短路。</summary>
        Point lastCursor = NoCursor;

        Size lastSelection = Size.Empty;

        public Magnifier()
        {
            InitializeComponent();

            // UI 线程定时器：回调本来就在 UI 线程，省掉每帧两次同步 Invoke，而且窗口
            // 销毁后不可能再触发——原实现用线程池定时器加一句 IsHandleCreated 判断，
            // 截图收尾时那是个 ObjectDisposedException 竞态。
            timer = new Timer
            {
                Enabled = false,
                Interval = FallbackIntervalMs
            };
            timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// 鼠标穿透。跟随光标后窗口会出现在光标近旁，不穿透就会吞掉截图窗口的拖拽消息。
        /// 辅助线窗口（<see cref="CaptureSelLine"/>）用的是同一招。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; //WS_EX_TRANSPARENT
                return cp;
            }
        }

        /// <summary>
        /// 位置、信息标签、画面的唯一更新入口。<paramref name="selection"/> 是当前选区尺寸，
        /// 没在拖拽时传 <see cref="Size.Empty"/>。
        /// <para>
        /// 光标没动就不重新取样、不重排窗口，所以静止时这里几乎零开销；标签还取决于选区
        /// 尺寸，因此它的短路条件比画面宽一档。
        /// </para>
        /// </summary>
        public void Track(Point cursor, Size selection)
        {
            if (cursor == lastCursor && selection == lastSelection)
            {
                return;
            }

            var cursorMoved = cursor != lastCursor;

            lastCursor = cursor;
            lastSelection = selection;

            SetText(cursor.X, cursor.Y, selection.Width, selection.Height);

            if (!cursorMoved)
            {
                return;
            }

            var screen = CaptureForm.TargetScreen;

            if (screen == null)
            {
                return;
            }

            Location = MagnifierGeometry.WindowLocation(cursor, Size, screen.Bounds, ScaledGap());
            RenderFrom(CaptureForm.ImgSnap, screen.Bounds.Location, cursor);
        }

        /// <summary>
        /// 把 <paramref name="snapshot"/> 中以 <paramref name="cursor"/> 为中心的一小块放大画进
        /// 缓冲区。<paramref name="snapshotOrigin"/> 是快照左上角对应的屏幕坐标，换算与截图
        /// 窗口画背景那一套一致。
        /// <para>
        /// 取快照而不是取实屏，是跟随光标的前提：放大镜是 TopMost 的子窗体，跟到光标附近后
        /// 读实屏会把自己也拍进去，层层套娃。取景越出快照时只画交集，其余留背景色——原实现
        /// 直接拿可能为负的坐标去取屏，屏幕边缘取到什么并不确定。
        /// </para>
        /// </summary>
        public void RenderFrom(Image snapshot, Point snapshotOrigin, Point cursor)
        {
            if (snapshot == null || !EnsureBuffer())
            {
                return;
            }

            var canvas = buffer.Size;
            var viewport = MagnifierGeometry.ViewportSize(canvas, MagnifierGeometry.Magnification);
            var destination = MagnifierGeometry.DestinationRectangle(
                canvas, viewport, MagnifierGeometry.Magnification);
            var source = MagnifierGeometry.SourceRectangle(
                new Point(cursor.X - snapshotOrigin.X, cursor.Y - snapshotOrigin.Y), viewport);
            var region = MagnifierGeometry.Clip(
                source, snapshot.Size, destination, MagnifierGeometry.Magnification);

            bufferGraphics.Clear(BackColor);

            if (!region.IsEmpty)
            {
                bufferGraphics.DrawImage(snapshot, region.Destination, region.Source, GraphicsUnit.Pixel);
            }

            // 必须同步画掉，不能只 Invalidate。WM_PAINT 的优先级低于鼠标输入，快速移动
            // 时它会被源源不断的 WM_MOUSEMOVE 一直挤在后面，画面要等输入停顿才更新一次，
            // 看起来就是「突然切换、中间过程全丢」。辅助线窗口
            // （CaptureSelLine.SetSelSize）用的就是 Invalidate + Update 这一对。
            pictureBox1.Invalidate();
            pictureBox1.Update();
        }

        /// <summary>
        /// 间隙按窗口所在显示器的 DPI 折算。放大镜是物理表面
        /// （<see cref="Window.DpiPolicy.PhysicalSurface"/>），框架不替它缩放任何东西，
        /// 所以这里显式换算：写死 24 在高 DPI 屏上就显得贴太近。
        /// <para>
        /// 不用 <c>Control.DeviceDpi</c>，理由见 <see cref="WindowsAPI.GetWindowDpi"/>；
        /// DPI 取不到时 <see cref="DpiContext"/> 原样返回基线值，不去猜一个倍率。
        /// </para>
        /// </summary>
        int ScaledGap()
        {
            var context = new DpiContext(WindowsAPI.GetMonitorSnapshotForWindow(Handle));
            var gap = context.LogicalToPhysicalLengthX(MagnifierGeometry.DefaultGap);

            return gap < 1 ? 1 : gap;
        }

        /// <summary>
        /// 整个会话共用一张后备位图，只在 PictureBox 尺寸变化时重建。尺寸取运行时
        /// ClientSize 而不是设计稿常量：虽然放大镜是物理表面、跨屏时
        /// <c>BaseForm.WndProc</c> 会把外框写回去，取景范围仍应以实际客户区为准。
        /// </summary>
        bool EnsureBuffer()
        {
            var size = pictureBox1.ClientSize;

            if (size.Width < 1 || size.Height < 1)
            {
                return false;
            }

            if (buffer != null && buffer.Size == size)
            {
                return true;
            }

            ReleaseBuffer();

            buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            bufferGraphics = Graphics.FromImage(buffer);
            bufferGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;

            // 最近邻放大要配 Half，否则整幅画面偏半个源像素，边缘一行一列错位。
            bufferGraphics.PixelOffsetMode = PixelOffsetMode.Half;

            // 只在这里赋一次值。此后每帧直接画进 buffer 再 Invalidate，控件图像不再被替换，
            // 于是「替换时要释放旧图像」这件事根本不存在——原实现每帧漏一张。
            // 前提是绘制与刷新都在 UI 线程上串行发生；一旦把刷新挪回后台线程，这个前提就
            // 不再成立。
            pictureBox1.Image = buffer;

            return true;
        }

        void ReleaseBuffer()
        {
            if (pictureBox1 != null && !pictureBox1.IsDisposed)
            {
                pictureBox1.Image = null;
            }

            if (bufferGraphics != null)
            {
                bufferGraphics.Dispose();
                bufferGraphics = null;
            }

            if (buffer != null)
            {
                buffer.Dispose();
                buffer = null;
            }
        }

        /// <summary>
        /// 按当前光标位置重新落位并重画。清掉上一次会话的记录，所以这一帧一定不会被
        /// 短路掉——每次截图开始都要走一次，否则窗口会停在上一次的位置。
        /// </summary>
        public void Reseat()
        {
            lastCursor = NoCursor;
            lastSelection = Size.Empty;

            Track(Cursor.Position, Size.Empty);
        }

        /// <summary>确定性释放：缓冲区、它的绘图对象、兜底定时器。由设计器生成的
        /// <c>Dispose(bool)</c> 调用，只走一次。</summary>
        protected override void DisposeOwnedResources()
        {
            base.DisposeOwnedResources();

            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
            }

            ReleaseBuffer();
        }

        void SetText(int x, int y, int width, int height)
        {
            label1.Text = string.Format("X:{0}, Y:{1}, W:{2}, H:{3}", x, y, width, height);
        }

        private void Magnifier_VisibleChanged(object sender, EventArgs e)
        {
            timer.Enabled = Visible;

            if (!Visible)
            {
                return;
            }

            TopMost = true;

            Reseat();
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            // 兜底而已：光标没动 Track 立刻返回，静止时这里的开销就是一次坐标比较。
            Track(Cursor.Position, lastSelection);
        }
    }
}
