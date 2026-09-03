using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using SETUNA.Main.StyleItems;
using SETUNA.Main.Window;

namespace SETUNA.Main
{
    // Token: 0x02000083 RID: 131
    internal class StyleItemListBox : SetunaListBox
    {
        // Token: 0x170000A9 RID: 169
        // (get) Token: 0x0600045D RID: 1117 RVA: 0x0001C809 File Offset: 0x0001AA09
        // (set) Token: 0x0600045C RID: 1116 RVA: 0x0001C800 File Offset: 0x0001AA00
        [Browsable(true)]
        [Description("アイテム説明用のフォントです。")]
        public Font HelpFont { get; set; }

        // Token: 0x170000AA RID: 170
        // (get) Token: 0x0600045F RID: 1119 RVA: 0x0001C81A File Offset: 0x0001AA1A
        // (set) Token: 0x0600045E RID: 1118 RVA: 0x0001C811 File Offset: 0x0001AA11
        [Description("アイテム説明用フォント色です。")]
        [Browsable(true)]
        public Color HelpForeColor { get; set; }

        // Token: 0x170000AB RID: 171
        // (get) Token: 0x06000461 RID: 1121 RVA: 0x0001C82B File Offset: 0x0001AA2B
        // (set) Token: 0x06000460 RID: 1120 RVA: 0x0001C822 File Offset: 0x0001AA22
        [Browsable(true)]
        [Description("終端アイテムで表示を無効にするか。")]
        public bool TerminateEnd { get; set; }

        /// <summary>
        /// 图标的绘制尺寸。逻辑基线是资源位图自己的 32x32（<c>Resources.Icon_*</c> 全是这个
        /// 尺寸），随 <see cref="ScaleControl"/> 换档，因此高 DPI 下图标是放大后的位图而不是
        /// 一枚缩在大行里的小图。样式图标是界面图形，不是用户内容，所以它跟着界面缩放。
        /// </summary>
        protected Size IconSize { get; private set; }

        // Token: 0x06000462 RID: 1122 RVA: 0x0001C833 File Offset: 0x0001AA33
        public StyleItemListBox()
        {
            ItemHeight = 39;
            base.LeftSpace = 34;
            IconSize = new Size(32, 32);
            TerminateEnd = false;
            HelpFont = new Font(Font, FontStyle.Regular);
            HelpForeColor = Color.Gray;
        }

        /// <summary>
        /// 见 <see cref="SetunaListBox.ScaleControl"/>：行高与留白之外，本类还持有图标尺寸。
        /// </summary>
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);

            IconSize = new Size(
                DpiContext.Scale(IconSize.Width, factor.Width),
                DpiContext.Scale(IconSize.Height, factor.Height));
        }

        /// <summary>
        /// <see cref="HelpFont"/> 不需要按 DPI 换算。
        /// <para>
        /// 它是以「点」为单位的字体（设计器给的是黑体 8pt），而点本身与 DPI 无关：
        /// <see cref="DrawItemString"/> 拿到的 <see cref="Graphics"/> 带着目标显示器的 DPI，
        /// 8pt 在 96 DPI 上就是 11 像素、在 168 DPI 上就是 19 像素，由 GDI+ 自己算。
        /// </para>
        /// <para>
        /// 手工重排年代必须换算，是因为当时 WinForms 的高 DPI 管线整个是关的，全窗体按系统 DPI
        /// 光栅化，这个属性又不在字体继承链上、换不到。官方管线接进来之后那个前提不成立了，
        /// 再乘一次 DPI 比例就是重复缩放。
        /// </para>
        /// <para>
        /// 行高、左侧留白、行内留白和图标尺寸则是像素度量，必须换档，见
        /// <see cref="SetunaListBox.ScaleControl"/>。
        /// </para>
        /// </summary>
        // Token: 0x06000463 RID: 1123 RVA: 0x0001C870 File Offset: 0x0001AA70
        protected override void DrawItemString(Graphics g, object item, Font font, Brush brush, Rectangle bounds, StringFormat sf, int index)
        {
            if (index < 0)
            {
                return;
            }
            var rectangle = bounds;
            string item2;
            string item3;
            if (!base.DesignMode)
            {
                var cstyleItem = (CStyleItem)item;
                item2 = cstyleItem.GetDisplayName();
                item3 = cstyleItem.GetDescription();
                var icon = cstyleItem.GetIcon();
                if (icon != null)
                {
                    // 放大一张 32px 的位图，双三次比最近邻明显干净。
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(
                        icon,
                        new Rectangle(ItemPadding, rectangle.Top + ItemPadding, IconSize.Width, IconSize.Height));
                }
            }
            else
            {
                item2 = item.ToString();
                item3 = item.ToString();
            }
            if (TerminateEnd && !base.DesignMode)
            {
                var terminate = GetTerminate();
                if (index > terminate && terminate >= 0)
                {
                    brush = Brushes.Gray;
                }
            }
            base.DrawItemString(g, item2, Font, brush, bounds, sf, index);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            sf.FormatFlags -= 4096;

            // 行距按 g 的 DPI 问，不是无参的 Font.GetHeight()：后者用的是进程那一档 DPI，
            // 在别的显示器上画同一个窗体时会偏。实测 168 DPI 的进程里，一个换到 96 DPI 的
            // 窗体上 10pt 字的无参行距仍报 27（实际渲染约 9），第二行的起点就被推低 18 像素。
            var lineHeight = (int)Font.GetHeight(g);
            bounds.Y += lineHeight + ItemPadding;
            bounds.Height = ItemHeight - (lineHeight + ItemPadding * 2);
            base.DrawItemString(g, item3, HelpFont, new SolidBrush(HelpForeColor), bounds, sf, index);
        }

        // Token: 0x06000464 RID: 1124 RVA: 0x0001C988 File Offset: 0x0001AB88
        protected int GetTerminate()
        {
            var result = -1;
            for (var i = 0; i < base.Items.Count; i++)
            {
                var cstyleItem = (CStyleItem)base.Items[i];
                if (cstyleItem.IsTerminate)
                {
                    result = i;
                    break;
                }
            }
            return result;
        }
    }
}
