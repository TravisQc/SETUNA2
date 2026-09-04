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

        /// <summary>
        /// 本窗体随所处显示器的 DPI 重排：只有说明文字与按键标签，没有位图与自绘控件，因此
        /// 全部交给框架——两个标签在设计器里显式指定的字体也一样，理由见
        /// <see cref="BaseForm.OnDpiContextChanged"/>。
        /// </summary>
        protected override DpiPolicy DpiPolicy => DpiPolicy.LogicalUi;

        // Token: 0x0600030B RID: 779 RVA: 0x000150E6 File Offset: 0x000132E6
        private void btnClose_Click(object sender, EventArgs e)
        {
            base.DialogResult = DialogResult.Cancel;
            base.Close();
        }

        // Token: 0x040001C2 RID: 450
        private Keys _key;
    }
}
