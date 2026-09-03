namespace SETUNA.Main
{
    // Token: 0x02000042 RID: 66
    public partial class ContextStyleMenuStrip : global::System.Windows.Forms.ContextMenuStrip
    {
        /// <summary>
        /// 菜单的 DPI 由框架负责，本类不插手。
        /// <para>
        /// 手工重排年代这里有一层 <c>ApplyMonitorDpi</c>：菜单是 component，从来不进
        /// <c>Control.Controls</c>，控件树遍历看不见它；下拉窗口又是系统在弹出那一刻才建的，
        /// 收不到 <c>WM_DPICHANGED</c>。当时实测同一个菜单弹到 168 DPI 主屏和 96 DPI 副屏上
        /// 尺寸一模一样（194x106、项高 32、字高 27 像素），即在 96 DPI 那块上整体大了 1.75 倍。
        /// </para>
        /// <para>
        /// net8 的 <c>ToolStrip</c> DPI 管线自己做这件事了。实测（<c>probes/MenuDpiProbe</c>）：
        /// 一个与本类配置完全相同的**普通** <c>ContextMenuStrip</c> 在 96 DPI 副屏上是
        /// 5.14pt/16px 字、图标列 16、项高 24、整体 123x52，在 168 DPI 主屏上是 9pt/27px、20、
        /// 34、153x72，嵌套下拉一并跟上。手工那一层加上去只把 96 DPI 那档的宽度从 123 改成
        /// 120，别的一个字节都没变，所以它现在只是重复劳动。
        /// </para>
        /// <para>
        /// 一处已知的不一致留在框架侧：设计器给的 <c>ImageScalingSize = 20x20</c> 在系统 DPI
        /// 那块显示器上原样保留，换到另一档时框架会按它自己的 16x16 默认值重算（96 DPI 上得到
        /// 16，而按设计值折算应当是 11）。眼下两个菜单里没有一个项带图标，所以看不出来；真要
        /// 加图标，得先回答「20 是哪一档 DPI 的设计值」，再决定要不要把它交给框架。
        /// 按点取显示器 DPI 的能力（<c>WindowsAPI.GetMonitorDpiAt</c>）仍然保留，还没有窗口
        /// 句柄的弹出物需要它，<c>MenuDpiTests</c> 单独钉住这一条。
        /// </para>
        /// </summary>
        // Token: 0x0600026D RID: 621 RVA: 0x0000D1F3 File Offset: 0x0000B3F3
        public ContextStyleMenuStrip()
        {
            InitializeComponent();
        }

        // Token: 0x0600026E RID: 622 RVA: 0x0000D1FB File Offset: 0x0000B3FB
        public ContextStyleMenuStrip(global::System.ComponentModel.IContainer container)
        {
            container.Add(this);

            InitializeComponent();
        }

        // Token: 0x17000068 RID: 104
        // (get) Token: 0x06000270 RID: 624 RVA: 0x0000D20D File Offset: 0x0000B40D
        // (set) Token: 0x0600026F RID: 623 RVA: 0x0000D204 File Offset: 0x0000B404
        public ScrapBase Scrap
        {
            get => _scrap;
            set => _scrap = value;
        }

        // Token: 0x04000111 RID: 273
        private ScrapBase _scrap;

        private void ContextStyleMenuStrip_Opened(object sender, System.EventArgs e)
        {
            Layer.LayerManager.Instance.SuspendRefresh();
        }

        private void ContextStyleMenuStrip_Closed(object sender, System.Windows.Forms.ToolStripDropDownClosedEventArgs e)
        {
            Layer.LayerManager.Instance.ResumeRefresh();
        }
    }
}
