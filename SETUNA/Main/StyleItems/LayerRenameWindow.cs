using System.ComponentModel;
using System.Windows.Forms;
using SETUNA.Main.Localization;

namespace SETUNA.Main.StyleItems
{
    // Token: 0x02000028 RID: 40
    public partial class LayerRenameWindow : BaseForm
    {
        // Token: 0x1700004B RID: 75
        // (get) Token: 0x0600019C RID: 412 RVA: 0x000095CA File Offset: 0x000077CA
        // (set) Token: 0x0600019B RID: 411 RVA: 0x000095BC File Offset: 0x000077BC
        public string LayerName
        {
            get => txtLayerName.Text;
            set => txtLayerName.Text = value;
        }

        // Token: 0x0600019D RID: 413 RVA: 0x000095D7 File Offset: 0x000077D7
        public LayerRenameWindow()
        {
            InitializeComponent();
        }

        // 本窗体刻意不参与跨显示器重排（继承 BaseForm 的默认值 false）。
        //
        // 它的客户区高度本来就不由自动缩放倍率决定：实测原生排版在 96 DPI 下是 236×59、
        // 在 168 DPI 下是 433×74，比值 1.25 与倍率 1.75 相去甚远。两个 DPI 下按钮都已经越出
        // 客户区（96 DPI 下按钮底边在 74，客户区只有 59；168 DPI 下底边 129、客户区 74），
        // 也就是说这个对话框的按钮在任何缩放比例下都被裁掉一截——一个与跨显示器无关的既有缺陷。
        // 在这种窗体上重排只会把裁切放大（按倍率算出的客户区是 42，比原生的 59 还少 17），
        // 所以先保持原有行为，等那个既有缺陷修好、排版变成倍率的函数之后再接入。

        // Token: 0x0600019E RID: 414 RVA: 0x000095E5 File Offset: 0x000077E5
        private void textBox1_Validating(object sender, CancelEventArgs e)
        {
            if (txtLayerName.TextLength == 0)
            {
                errorProvider1.SetIconAlignment(txtLayerName, ErrorIconAlignment.TopLeft);
                errorProvider1.SetError(txtLayerName, Lang.T("Message.LayerNameRequired"));
            }
        }
    }
}
