using System;
using System.Windows.Forms;
using SETUNA.Main.Window;

namespace SETUNA.Main.StyleItems
{
    // Token: 0x02000002 RID: 2
    public partial class ToolBoxForm : BaseForm
    {
        // Token: 0x06000003 RID: 3 RVA: 0x00002285 File Offset: 0x00000485
        public ToolBoxForm()
        {
            InitializeComponent();
        }

        // Token: 0x06000004 RID: 4 RVA: 0x00002293 File Offset: 0x00000493
        public ToolBoxForm(object style)
        {
            InitializeComponent();
            SetStyleToForm(style);
        }

        /// <summary>
        /// 样式设置对话框全部随所处显示器的 DPI 重排。
        /// <para>
        /// 一处重写覆盖全部十七个面板：它们都派生自本类，都是普通的设置对话框——
        /// <c>AutoScaleMode.Font</c> 加固定边框，排版完全由「设计值 + DPI」决定，正是
        /// <c>BaseForm</c> 的重排能精确还原的那一类。只有 JPEG 预览面板把边框改成了
        /// <c>SizableToolWindow</c>，它因此走 <see cref="BaseForm.ReproducesLayoutFromBaseline"/>
        /// 的另一条分支，按新旧 DPI 之比缩放当前状态，不受基线快照约束。
        /// </para>
        /// <para>
        /// 以像素为语义的那部分已经被 <c>AutoSize</c> 挡住了：JPEG/PNG 预览面板里的
        /// <c>picPreview</c> 是 <c>SizeMode = AutoSize</c>，尺寸由编码出来的位图决定，重排写回
        /// 矩形对它无效，于是预览始终是 1:1 像素——要拿它判断压缩质量，这一点不能变。跟着缩放的
        /// 只有外面那圈裁剪用的面板。
        /// </para>
        /// </summary>
        protected override DpiPolicy DpiPolicy => DpiPolicy.LogicalUi;

        // Token: 0x06000005 RID: 5 RVA: 0x000022A8 File Offset: 0x000004A8
        private void cmdOK_Click(object sender, EventArgs e)
        {
            var flag = false;
            OKCheck(ref flag);
            if (flag)
            {
                return;
            }
            base.DialogResult = DialogResult.OK;
            base.Close();
        }

        // Token: 0x06000006 RID: 6 RVA: 0x000022D0 File Offset: 0x000004D0
        private void cmdCancel_Click(object sender, EventArgs e)
        {
            base.DialogResult = DialogResult.Cancel;
            base.Close();
        }

        // Token: 0x17000001 RID: 1
        // (get) Token: 0x06000007 RID: 7 RVA: 0x000022DF File Offset: 0x000004DF
        public object StyleItem => GetStyleFromForm();

        // Token: 0x06000008 RID: 8 RVA: 0x000022E7 File Offset: 0x000004E7
        protected virtual void SetStyleToForm(object style)
        {
            throw new Exception("SetStyleToForm未实现");
        }

        // Token: 0x06000009 RID: 9 RVA: 0x000022F3 File Offset: 0x000004F3
        protected virtual object GetStyleFromForm()
        {
            throw new Exception("GetStyleFromForm未实现");
        }

        // Token: 0x0600000A RID: 10 RVA: 0x000022FF File Offset: 0x000004FF
        protected virtual void OKCheck(ref bool cancel)
        {
        }
    }
}
