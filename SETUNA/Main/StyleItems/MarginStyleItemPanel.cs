using System;
using System.Drawing;
using System.Windows.Forms;
using SETUNA.Main.Common;

namespace SETUNA.Main.StyleItems
{
    // Token: 0x02000003 RID: 3
    internal partial class MarginStyleItemPanel : ToolBoxForm
    {
        // Token: 0x0600000B RID: 11 RVA: 0x00002301 File Offset: 0x00000501
        public MarginStyleItemPanel(CMarginStyleItem style) : base(style)
        {
        }

        // Token: 0x0600000D RID: 13 RVA: 0x00002AF8 File Offset: 0x00000CF8
        protected override void SetStyleToForm(object style)
        {
            var cmarginStyleItem = (CMarginStyleItem)style;
            InitializeComponent();
            imgBackground = PreviewBackdrop.Capture(picPreview.Size);
            imgScrap = SETUNA.Resources.Image.SampleImage;
            picSample = new PictureBox
            {
                Image = imgScrap,
                Size = imgScrap.Size
            };
            frmPrev = new Form
            {
                TopLevel = false,
                Parent = picPreview,
                Text = "sample"
            };
            frmPrev.Controls.Add(picSample);
            ResizeSample();
            frmPrev.Show();
            numMargin.Value = cmarginStyleItem.MarginSize;
            pictureBox1.BackColor = cmarginStyleItem.GetMarginColor();
            chkTopMost.Checked = cmarginStyleItem.TopMost;
            switch (cmarginStyleItem.BorderStyle)
            {
                case 0:
                    rdo3D.Checked = true;
                    break;
                case 1:
                    rdoSolid.Checked = true;
                    break;
                case 2:
                    rdoWindow.Checked = true;
                    break;
                default:
                    throw new Exception();
            }
            Text = cmarginStyleItem.GetDisplayName();
            rdoWindow_CheckedChanged(null, null);
            frmPrev.Paint += Sample_Paint;
        }

        /// <summary>
        /// 预览里的两样东西跟上重排结果：按预览框尺寸抓的背景，以及浮在背景上那个示例窗口。
        /// <para>
        /// 示例窗口是个 <c>TopLevel = false</c> 的子窗体，连同里面的 <c>picSample</c> 一起挂在
        /// <c>picPreview</c> 下，所以 <c>AutoScaleMode.Font</c> 会把它们的矩形一并放大。可它们
        /// 表示的是「贴图加上 N 像素边距之后的样子」，边距的单位就是像素——放大之后示例图周围
        /// 会多出一圈与设定值无关的空白，<see cref="ResizeSample"/> 又按 <c>picSample.Width</c>
        /// 反算边框，误差会一路传下去。因此把 <c>picSample</c> 按原图尺寸钉回去，再让
        /// <see cref="ResizeSample"/> 重新算一遍边框与居中位置——它读的是 <c>picPreview</c> 的
        /// 当前尺寸，所以放大后的预览框里仍然居中。
        /// </para>
        /// </summary>
        protected override void OnDpiRelayout(int newDpi, int oldDpi)
        {
            base.OnDpiRelayout(newDpi, oldDpi);

            imgBackground = PreviewBackdrop.Resize(imgBackground, picPreview.Size);

            if (picSample != null && imgScrap != null)
            {
                picSample.Size = imgScrap.Size;
            }

            ResizeSample();
            picPreview.Invalidate();
        }

        // Token: 0x0600000E RID: 14 RVA: 0x00002CCC File Offset: 0x00000ECC
        protected override object GetStyleFromForm()
        {
            var cmarginStyleItem = new CMarginStyleItem
            {
                MarginSize = (int)numMargin.Value
            };
            cmarginStyleItem.SetMarginColor(pictureBox1.BackColor);
            if (rdoWindow.Checked)
            {
                cmarginStyleItem.BorderStyle = 2;
            }
            else if (rdoSolid.Checked)
            {
                cmarginStyleItem.BorderStyle = 1;
            }
            else if (rdo3D.Checked)
            {
                cmarginStyleItem.BorderStyle = 0;
            }
            cmarginStyleItem.TopMost = chkTopMost.Checked;
            return cmarginStyleItem;
        }

        // Token: 0x0600000F RID: 15 RVA: 0x00002D58 File Offset: 0x00000F58
        private void pictureBox1_Click(object sender, EventArgs e)
        {
            var dialogResult = colorDialog1.ShowDialog();
            if (dialogResult == DialogResult.OK)
            {
                pictureBox1.BackColor = colorDialog1.Color;
                rdoWindow_CheckedChanged(null, null);
            }
        }

        // Token: 0x06000010 RID: 16 RVA: 0x00002D93 File Offset: 0x00000F93
        private void picPreview_Paint(object sender, PaintEventArgs e)
        {
            if (imgBackground != null)
            {
                e.Graphics.DrawImageUnscaled(imgBackground, new Point(0, 0));
            }
        }

        // Token: 0x06000011 RID: 17 RVA: 0x00002DB5 File Offset: 0x00000FB5
        private void MarginStyleItemPanel_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (imgBackground != null)
            {
                imgBackground.Dispose();
                imgBackground = null;
                GC.Collect();
            }
        }

        // Token: 0x06000012 RID: 18 RVA: 0x00002DD8 File Offset: 0x00000FD8
        private void ResizeSample()
        {
            if (frmPrev == null)
            {
                return;
            }
            frmPrev.MinimizeBox = false;
            frmPrev.MaximizeBox = false;
            frmPrev.ControlBox = false;
            if (rdoWindow.Checked)
            {
                frmPrev.ClientSize = new Size(picSample.Width + 1, picSample.Height + 1);
                frmPrev.BackColor = SystemColors.Control;
                picSample.Left = 0;
                picSample.Top = 0;
            }
            else if (rdoSolid.Checked)
            {
                frmPrev.ClientSize = new Size(picSample.Width + (int)numMargin.Value * 2, picSample.Height + (int)numMargin.Value * 2);
                frmPrev.BackColor = pictureBox1.BackColor;
                picSample.Left = (int)numMargin.Value;
                picSample.Top = (int)numMargin.Value;
            }
            else if (rdo3D.Checked)
            {
                frmPrev.ClientSize = new Size(picSample.Width + 2, picSample.Height + 2);
                frmPrev.BackColor = Color.FromArgb(25, 25, 25);
                picSample.Left = 1;
                picSample.Top = 1;
            }
            frmPrev.Left = (picPreview.Width - frmPrev.Width) / 2;
            frmPrev.Top = (picPreview.Height - frmPrev.Height) / 2;
            frmPrev.Refresh();
        }

        // Token: 0x06000013 RID: 19 RVA: 0x00002FD8 File Offset: 0x000011D8
        private void rdoWindow_CheckedChanged(object sender, EventArgs e)
        {
            if (rdoWindow.Checked)
            {
                frmPrev.FormBorderStyle = FormBorderStyle.FixedDialog;
            }
            else if (rdoSolid.Checked)
            {
                frmPrev.FormBorderStyle = FormBorderStyle.None;
            }
            else if (rdo3D.Checked)
            {
                frmPrev.FormBorderStyle = FormBorderStyle.None;
            }
            ResizeSample();
        }

        // Token: 0x06000014 RID: 20 RVA: 0x0000303C File Offset: 0x0000123C
        private void Sample_Paint(object sender, PaintEventArgs e)
        {
            if (rdo3D.Checked)
            {
                using (var pen = new Pen(Color.FromArgb(243, 243, 243)))
                {
                    e.Graphics.DrawLine(pen, 0, 0, 0, frmPrev.Height);
                    e.Graphics.DrawLine(pen, 0, 0, frmPrev.Width, 0);
                }
            }
        }

        // Token: 0x0400000C RID: 12
        private Form frmPrev;

        // Token: 0x0400000D RID: 13
        private PictureBox picSample;

        // Token: 0x0400000E RID: 14
        private System.Drawing.Image imgBackground;

        // Token: 0x04000013 RID: 19
        private System.Drawing.Image imgScrap;
    }
}
