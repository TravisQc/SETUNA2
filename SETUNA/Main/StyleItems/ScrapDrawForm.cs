using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SETUNA.Main.Window;

namespace SETUNA.Main.StyleItems
{
    // Token: 0x02000022 RID: 34
    public partial class ScrapDrawForm : BaseForm
    {
        protected override DpiPolicy DpiPolicy => DpiPolicy.PhysicalSurface;
        // Token: 0x06000162 RID: 354
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr ReleaseDC(IntPtr hwnd, IntPtr hdc);

        // Token: 0x06000163 RID: 355
        [DllImport("gdi32.dll")]
        private static extern int BitBlt(IntPtr hDestDC, int x, int y, int nWidth, int nHeight, IntPtr hSrcDC, int xSrc, int ySrc, int dwRop);

        // Token: 0x06000165 RID: 357 RVA: 0x00008430 File Offset: 0x00006630
        private ScrapDrawForm()
        {
            InitializeComponent();
        }

        // Token: 0x06000166 RID: 358 RVA: 0x00008440 File Offset: 0x00006640
        /// <summary>
        /// 画布比贴图每边宽出 50 像素，让用户可以画到贴图之外一点。**这 50 是图像像素，
        /// 刻意不随 DPI 变**：这一圈是被压暗的桌面内容，与贴图位图拼在同一个
        /// <c>_baseimg</c> 里，按界面缩放放大它就等于把桌面截图拉伸了。窗口本身是
        /// <c>PhysicalSurface</c>，尺寸直接取 <c>_baseimg</c> 的像素尺寸。
        /// </summary>
        public ScrapDrawForm(ScrapBase scrap)
        {
            InitializeComponent();
            _scrap = scrap;
            _basepoint = new Point(50, 50);
            var p = new Point(scrap.ClientRectangle.X + scrap.ClientSize.Width / 2, scrap.ClientRectangle.Y + scrap.ClientSize.Height / 2);
            p = scrap.PointToScreen(p);
            _baseimg = new Bitmap(scrap.Image.Width + 100, scrap.Image.Height + 100, PixelFormat.Format24bppRgb);
            CopyFromScreen(_baseimg, new Point(p.X - scrap.Image.Width / 2 - 50, p.Y - scrap.Image.Height / 2 - 50));
            var colorMatrix = new ColorMatrix();
            var num = 0.298912f;
            var num2 = 0.586611f;
            var num3 = 0.114478f;
            colorMatrix[0, 0] = (colorMatrix[0, 1] = (colorMatrix[0, 2] = num * 0.5f));
            colorMatrix[1, 0] = (colorMatrix[1, 1] = (colorMatrix[1, 2] = num2 * 0.5f));
            colorMatrix[2, 0] = (colorMatrix[2, 1] = (colorMatrix[2, 2] = num3 * 0.5f));
            colorMatrix[3, 3] = (colorMatrix[4, 4] = 1f);
            var imageAttributes = new ImageAttributes();
            imageAttributes.SetColorMatrix(colorMatrix);
            using (var graphics = Graphics.FromImage(_baseimg))
            {
                var p2 = new Point(scrap.ClientRectangle.Left, scrap.ClientRectangle.Top);
                p2 = scrap.PointToScreen(p2);
                graphics.DrawImageUnscaled(scrap.Image, 50, 50);
                graphics.DrawImage(_baseimg, new Rectangle(0, 0, _baseimg.Width, _baseimg.Height), 0, 0, _baseimg.Width, _baseimg.Height, GraphicsUnit.Pixel, imageAttributes);
            }
            base.Width = _baseimg.Width;
            base.Height = _baseimg.Height;
            base.Left = p.X - base.Width / 2;
            base.Top = p.Y - base.Height / 2;
            _scrap.Hide();
        }

        // Token: 0x06000167 RID: 359 RVA: 0x000086F8 File Offset: 0x000068F8
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_baseimg != null)
            {
                e.Graphics.DrawImageUnscaled(_baseimg, 0, 0);
            }
        }

        // Token: 0x06000168 RID: 360 RVA: 0x00008718 File Offset: 0x00006918
        public bool CopyFromScreen(Image img, Point location)
        {
            var screenDC = IntPtr.Zero;
            try
            {
                screenDC = ScrapDrawForm.GetDC(IntPtr.Zero);
                using (var graphics = Graphics.FromImage(img))
                {
                    var targetDC = IntPtr.Zero;
                    try
                    {
                        targetDC = graphics.GetHdc();
                        ScrapDrawForm.BitBlt(targetDC, 0, 0, img.Width, img.Height, screenDC, location.X, location.Y, ScrapDrawForm.SRCCOPY | ScrapDrawForm.CAPTUREBLT);
                    }
                    finally
                    {
                        if (targetDC != IntPtr.Zero)
                        {
                            graphics.ReleaseHdc(targetDC);
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
            finally
            {
                // 与 CaptureForm.CopyFromScreen 同理：GetDC 按约定配 ReleaseDC。
                if (screenDC != IntPtr.Zero)
                {
                    ScrapDrawForm.ReleaseDC(IntPtr.Zero, screenDC);
                }
            }
        }

        // Token: 0x06000169 RID: 361 RVA: 0x0000880C File Offset: 0x00006A0C
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_scrap != null)
            {
                _scrap.Show();
            }
        }

        // Token: 0x0400009F RID: 159
        private const int SRCCOPY = 13369376;

        // Token: 0x040000A0 RID: 160
        private const int CAPTUREBLT = 1073741824;

        // Token: 0x040000A1 RID: 161
        protected const int Space = 50;

        // Token: 0x040000A4 RID: 164
        protected ScrapBase _scrap;

        // Token: 0x040000A5 RID: 165
        private Point _basepoint;
    }
}
