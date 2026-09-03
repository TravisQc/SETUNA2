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
        /// <see cref="HelpFont"/> 跟着 <see cref="Control.Font"/> 一起换档，按两者字号之比。
        /// <para>
        /// 2026-09-03 实测（`probes/DialogRelayoutProbe`，`DIALOG_PROBE_MEASURE_OWNERDRAW=1`，把窗体
        /// 真的放到各块显示器上而不是发合成消息）：**控件自己的 <see cref="Graphics"/> 无论窗口在哪块
        /// 显示器上都报进程那一档 DPI**（本机恒为 168），所以 GDI+ 把点值换成像素用的是进程 DPI、
        /// 点值不会自己跟随显示器；让文字跟随显示器的唯一机制就是把点值乘上 DPI 之比。原先这里的注释
        /// 断言相反，是错的。
        /// </para>
        /// <para>
        /// 而框架碰不到 <see cref="HelpFont"/>——它不是 <see cref="Control.Font"/>。原先由
        /// <c>StyleEditForm.OnDpiContextChanged</c> 换算，那条路只在**换档**时走：窗体直接建在 96 DPI
        /// 副屏上时首次建立上下文不发通知，实测 <see cref="Control.Font"/> 是 5.71pt 而
        /// <see cref="HelpFont"/> 仍是 8pt，说明文字比标题文字还大（渲染 21px vs 15px），两行合计 36px
        /// 塞进 39px 的行里。
        /// </para>
        /// <para>
        /// **不能改用 <see cref="ScaleControl"/> 的倍率**，试过：构造期的 <c>PerformAutoScale</c> 会调
        /// <see cref="ScaleControl"/>、却不缩放显式指定的 <see cref="Control.Font"/>，于是 168 DPI 上
        /// <see cref="HelpFont"/> 被乘成 14pt 而 <see cref="Control.Font"/> 还是 10pt——两者虽然跨屏一致
        /// 了，比例却整体偏了 1.75 倍。挂在 <see cref="OnFontChanged"/> 上才是与 <see cref="Control.Font"/>
        /// 严格同步的那一处：谁改了主字体，说明字体就按同一个比例走。
        /// </para>
        /// <para>
        /// 只释放本方法造过的那一份：设计器造的原件可能另有引用，不归这里管。
        /// </para>
        /// </summary>
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);

            var size = Font == null ? 0f : Font.Size;
            var previous = _lastFontSize;
            _lastFontSize = size;

            if (previous <= 0f || size <= 0f || previous == size)
            {
                return;
            }

            HelpFont = ScaleHelpFont(size / previous);
        }

        Font ScaleHelpFont(float factor)
        {
            var current = HelpFont;
            if (current == null || factor <= 0f || factor == 1f)
            {
                return current;
            }

            var size = current.Size * factor;
            if (size <= 0f || float.IsNaN(size) || float.IsInfinity(size))
            {
                return current;
            }

            var scaled = new Font(
                current.FontFamily, size, current.Style, current.Unit, current.GdiCharSet, current.GdiVerticalFont);

            if (ReferenceEquals(current, _ownedHelpFont))
            {
                current.Dispose();
            }

            _ownedHelpFont = scaled;

            return scaled;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownedHelpFont != null)
            {
                if (ReferenceEquals(HelpFont, _ownedHelpFont))
                {
                    HelpFont = null;
                }

                _ownedHelpFont.Dispose();
                _ownedHelpFont = null;
            }

            base.Dispose(disposing);
        }

        Font _ownedHelpFont;
        float _lastFontSize;

        /// <summary>
        /// 一行画两段文字：<see cref="Control.Font"/> 画名称，<see cref="HelpFont"/> 画说明，
        /// 两者的换档都在 <see cref="ScaleControl"/> 里，理由见 <see cref="ScaleHelpFont"/>。
        /// <para>
        /// 这里原先写着「<see cref="HelpFont"/> 不需要按 DPI 换算，因为 <see cref="Graphics"/> 带着
        /// 目标显示器的 DPI，8pt 在 96 DPI 上就是 11 像素」——**实测是错的**，控件的
        /// <see cref="Graphics"/> 恒报进程那一档 DPI。
        /// </para>
        /// <para>
        /// 行高、左侧留白、行内留白和图标尺寸是像素度量，同样在
        /// <see cref="SetunaListBox.ScaleControl"/> 里换档。
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
