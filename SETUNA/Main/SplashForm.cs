using System;
using System.Windows.Forms;
using SETUNA.Main.Common;

namespace SETUNA.Main
{
    // Token: 0x02000043 RID: 67
    public partial class SplashForm : BaseForm
    {
        // Token: 0x06000273 RID: 627 RVA: 0x0000D537 File Offset: 0x0000B737
        public SplashForm()
        {
            InitializeComponent();

            lblVer.Text = base.ProductName + " " + Application.ProductVersion;
            label1.Text = URLUtils.NewURL;

            // Logo 素材是 400x126，控件按环境字体缩放后在 175% 下约 758x244，等于放大 1.9 倍。
            // 设计器给的 SizeMode = Zoom 走 GDI+ 默认插值，放大出来是糊的。
            SmoothImage.Attach(pictureBox1);
        }

        // Token: 0x06000274 RID: 628 RVA: 0x0000D565 File Offset: 0x0000B765
        private void SplashForm_Load(object sender, EventArgs e)
        {
        }

        // Token: 0x06000275 RID: 629 RVA: 0x0000D567 File Offset: 0x0000B767
        private void SplashTimer_Tick(object sender, EventArgs e)
        {
            base.Close();
        }

        // Token: 0x06000276 RID: 630 RVA: 0x0000D56F File Offset: 0x0000B76F
        private void pictureBox1_Click(object sender, EventArgs e)
        {
            SplashTimer.Enabled = false;
            base.Close();
        }
    }
}
