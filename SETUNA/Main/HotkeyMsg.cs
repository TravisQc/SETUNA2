using System;
using System.Windows.Forms;
using SETUNA.Main.Window;

namespace SETUNA.Main
{
    // Token: 0x02000051 RID: 81
    public partial class HotkeyMsg : BaseForm
    {
        // Token: 0x17000075 RID: 117
        // (set) Token: 0x06000309 RID: 777 RVA: 0x00015038 File Offset: 0x00013238
        public Keys HotKey
        {
            set
            {
                _key = value;
                var text = "";
                if ((_key & Keys.Control) == Keys.Control)
                {
                    text += "Ctrl + ";
                }
                if ((_key & Keys.Shift) == Keys.Shift)
                {
                    text += "Shift + ";
                }
                if ((_key & Keys.Alt) == Keys.Alt)
                {
                    text += "Alt + ";
                }
                text += (_key & Keys.KeyCode).ToString();
                lblKey.Text = text;
            }
        }

        // Token: 0x0600030A RID: 778 RVA: 0x000150D8 File Offset: 0x000132D8
        public HotkeyMsg()
        {
            InitializeComponent();
        }

        /// <summary>本窗体随所处显示器的 DPI 重排：只有说明文字与按键标签，没有位图与自绘控件。</summary>
        protected override DpiPolicy DpiPolicy => DpiPolicy.LogicalUi;

        /// <summary>
        /// 这两个标签在设计器里显式指定了字体（宋体 10pt 粗体 / 9pt 粗体），不在窗体的字体
        /// 继承链上，框架的缩放到不了它们——实测跨屏之后 <c>lblKey</c> 恒为 24px，而同一个
        /// 窗体里继承字体的控件从 16px 变到 27px。
        /// </summary>
        protected override void OnDpiContextChanged(int previousDpi)
        {
            base.OnDpiContextChanged(previousDpi);

            RescaleOwnedFonts(previousDpi, label1, lblKey);
        }

        // Token: 0x0600030B RID: 779 RVA: 0x000150E6 File Offset: 0x000132E6
        private void btnClose_Click(object sender, EventArgs e)
        {
            base.DialogResult = DialogResult.Cancel;
            base.Close();
        }

        // Token: 0x0600030C RID: 780 RVA: 0x000150F5 File Offset: 0x000132F5
        private void btnOption_Click(object sender, EventArgs e)
        {
            base.DialogResult = DialogResult.OK;
            base.Close();
        }

        // Token: 0x040001C2 RID: 450
        private Keys _key;
    }
}
