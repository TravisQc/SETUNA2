using System.Drawing;
using System.Windows.Forms;

namespace SETUNA.Main.StyleItems
{
    // Token: 0x020000AC RID: 172
    public class PenButton : RadioButton
    {
        // Token: 0x06000587 RID: 1415 RVA: 0x00026280 File Offset: 0x00024480
        public PenButton()
        {
            ButtonColor = Color.Black;
            base.Appearance = Appearance.Button;
        }

        // Token: 0x170000C5 RID: 197
        // (get) Token: 0x06000588 RID: 1416 RVA: 0x0002629A File Offset: 0x0002449A
        // (set) Token: 0x06000589 RID: 1417 RVA: 0x000262A2 File Offset: 0x000244A2
        public int PenSize
        {
            get => _pensize;
            set
            {
                _pensize = value;
                Refresh();
            }
        }

        // Token: 0x170000C6 RID: 198
        // (get) Token: 0x0600058A RID: 1418 RVA: 0x000262B1 File Offset: 0x000244B1
        // (set) Token: 0x0600058B RID: 1419 RVA: 0x000262B9 File Offset: 0x000244B9
        public Color ButtonColor
        {
            get => _penColor;
            set => _penColor = value;
        }

        /// <summary>
        /// 画一个 <see cref="PenSize"/> 像素的圆点。**这个尺寸刻意不随 DPI 变**：它是笔刷落在
        /// 贴图上的宽度，单位是图像像素（工具提示写的就是「20px」），按界面缩放放大之后显示的
        /// 就不再是用户会得到的那个笔宽了。按钮本身的矩形照常由框架缩放，所以高 DPI 下这个点
        /// 在按钮里占的比例会变小——那是正确的，因为图像像素和界面像素的比值本来就变了。
        /// </summary>
        // Token: 0x0600058C RID: 1420 RVA: 0x000262C4 File Offset: 0x000244C4
        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            pevent.Graphics.FillEllipse(new SolidBrush(_penColor), new Rectangle((base.Width - PenSize) / 2 - 1, (base.Height - PenSize - 1) / 2, PenSize, PenSize + 1));
        }

        // Token: 0x04000374 RID: 884
        private int _pensize;

        // Token: 0x04000375 RID: 885
        private Color _penColor;
    }
}
