using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using SETUNA.Main.StyleItems;
using SETUNA.Main.Window;

namespace SETUNA.Main
{
    // Token: 0x02000083 RID: 131
    internal class StyleItemListBox : SetunaListBox, IDpiRelayoutListener
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

        // Token: 0x06000462 RID: 1122 RVA: 0x0001C833 File Offset: 0x0001AA33
        public StyleItemListBox()
        {
            ItemHeight = 39;
            base.LeftSpace = 34;
            TerminateEnd = false;
            HelpFont = new Font(Font, FontStyle.Regular);
            HelpForeColor = Color.Gray;
        }

        /// <summary>
        /// DPI 变化时换算 <see cref="HelpFont"/>。
        /// <para>
        /// 它是独立的字体属性，不在控件树的字体继承链上，窗体重排换不到它。而它的像素大小本来
        /// 是随系统 DPI 变的：设计器给的 8pt 在 96 DPI 下是 11 像素、在 168 DPI 下是 19 像素。
        /// 重排把窗体其余部分换到了新 DPI 的等效字号，这里不跟上，说明文字就会停在旧 DPI 的
        /// 大小上，把每一项的两行文字挤成一团。
        /// </para>
        /// <para>
        /// <see cref="System.Windows.Forms.ListBox.ItemHeight"/>、
        /// <see cref="SetunaListBox.LeftSpace"/> 和图标的绘制尺寸刻意不换算：它们在原实现里
        /// 于任何 DPI 下都是同一个像素值（在 96 与 168 DPI 上实测均为 39 与 34），换算反而会
        /// 让重排结果偏离该 DPI 下的原生排版，并且把固定尺寸的图标挤出行高。行高不随 DPI 变化
        /// 是既有问题，与跨显示器重排无关。
        /// </para>
        /// </summary>
        public void OnDpiRelayout(int newDpi, int oldDpi)
        {
            if (HelpFont == null)
            {
                return;
            }

            var previous = HelpFont;
            HelpFont = DpiRelayout.ScaleFont(previous, newDpi, oldDpi);

            // 这个字体只由本属性持有（构造函数或设计器各自 new 出来的），换掉之后没人再引用，
            // 必须还回 GDI 句柄。
            previous.Dispose();
        }

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
                    g.DrawImage(icon, 2, rectangle.Top + 2);
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
            bounds.Y += (int)Font.GetHeight() + 2;
            bounds.Height = ItemHeight - ((int)Font.GetHeight() + 2 + 2);
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
