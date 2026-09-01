using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SETUNA.Main.Window;

namespace SETUNA.Main
{
    // Token: 0x02000042 RID: 66
    public partial class ContextStyleMenuStrip : global::System.Windows.Forms.ContextMenuStrip
    {
        /// <summary>
        /// 菜单按「已配置的样子」呈现时对应的 DPI，也就是进程启动时的系统 DPI。
        /// <para>
        /// 菜单字体来自 <c>ToolStripManager</c>，而它取自系统菜单字体，是真正按系统 DPI 实现出来的
        /// ——实测系统 DPI 为 168 时是 Microsoft YaHei UI 9pt、字高 27 像素，同一台机器上把进程的
        /// DPI 感知关掉（系统 DPI 报 96），同一个 9pt 字高变成 16 像素。所以字体的换算基准就是
        /// 系统 DPI，与 <c>BaseForm</c> 的重排基准是同一个量、同一个理由。
        /// </para>
        /// <para>
        /// <c>ImageScalingSize</c> 用同一个基准，但理由是另一条：它是设计器写死的像素值，任何 DPI
        /// 下都不变，也就是说它在系统 DPI 那一档「正好是现在这个样子」。以它为基准换算，等于
        /// 「系统 DPI 那块显示器上保持原样，别的显示器按比例跟着走」。这不等于原生：实测 DPI 不感知
        /// 的进程（等效原生 96 DPI）为同一个菜单留的图标列是 20 像素、项高 26，而按本基准算到
        /// 96 DPI 得到 11 像素、项高 22。差别落在图标列的宽度上，而眼下托盘菜单与参考图菜单里
        /// 没有一个项带图标（<c>ToolStripStyleButton</c> 带图片的那个重载没有调用点，参考图列表的
        /// 缩略图走 <c>ImageScaling = None</c>，都不受这个值影响），所以这几像素看不出来。真要往
        /// 菜单里加图标，得先重新回答「20 是 96 DPI 的设计值还是 168 DPI 的设计值」。
        /// </para>
        /// </summary>
        static readonly int BaselineDpi = WindowsAPI.GetSystemDpi();

        /// <summary>
        /// 基线度量：字体与图标尺寸在 <see cref="BaselineDpi"/> 那一档的取值，第一次弹出之前拍下。
        /// <para>
        /// 不能在构造函数里拍：设计器把 <c>ImageScalingSize</c> 写在<em>宿主</em>窗体的
        /// <c>InitializeComponent</c> 里（<c>Mainform.Designer.cs</c> 给两个菜单都设了 20x20），
        /// 那一句在本类的构造函数跑完之后才执行，构造时拍到的只是框架默认的 16x16。
        /// </para>
        /// <para>
        /// 每次都从这一份基线算到目标 DPI，而不是缩放上一次的结果：菜单会在两块显示器之间反复
        /// 弹出，逐次相乘会把取整误差累积起来——这与 <c>BaseForm</c> 保留排版基线快照是同一课。
        /// </para>
        /// </summary>
        Font baselineFont;
        Size baselineImageScalingSize;

        /// <summary>上一次换算创建的字体。换掉之后没人再引用它，必须还回 GDI 句柄。</summary>
        Font scaledFont;

        // Token: 0x0600026D RID: 621 RVA: 0x0000D1F3 File Offset: 0x0000B3F3
        public ContextStyleMenuStrip()
        {
            InitializeComponent();

            // Dispose(bool) 由设计器文件占着，不改生成文件，改挂事件。
            Disposed += ContextStyleMenuStrip_Disposed;
        }

        // Token: 0x0600026E RID: 622 RVA: 0x0000D1FB File Offset: 0x0000B3FB
        public ContextStyleMenuStrip(global::System.ComponentModel.IContainer container)
        {
            container.Add(this);

            InitializeComponent();

            Disposed += ContextStyleMenuStrip_Disposed;
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

        /// <summary>
        /// 弹出之前把度量换算到即将出现的那块显示器上。
        /// <para>
        /// 菜单走不了 <c>BaseForm</c> 那条路：它是 component，从来不进
        /// <c>Control.Controls</c>，重排遍历控件树时根本看不见它；而它的下拉窗口是系统在这一刻
        /// 之后才建的，也收不到 <c>WM_DPICHANGED</c>。实测同一个菜单弹到 168 DPI 主屏和 96 DPI
        /// 副屏上尺寸一模一样（194x106、项高 32、字高 27 像素），也就是说在 96 DPI 那块上整体
        /// 大了 1.75 倍。换算之后实测那块变成 137x76、项高 22、字高 16 像素，字号与 DPI 不感知
        /// 进程给出的原生 96 DPI 参考完全一致。
        /// </para>
        /// <para>
        /// 每次弹出都重算，而不是只在 DPI 变化时改一次：菜单每次都在光标处新建下拉窗口，本来就
        /// 没有「跟着窗口移动」这回事，按目标位置算反而更直接。
        /// </para>
        /// </summary>
        protected override void OnOpening(CancelEventArgs e)
        {
            ApplyMonitorDpi(WindowsAPI.GetMonitorDpiAt(Cursor.Position));

            base.OnOpening(e);
        }

        /// <summary>
        /// 把字体与图标尺寸换算到 <paramref name="targetDpi"/>。取不到 DPI 时什么都不做，
        /// 菜单保持已配置的样子。
        /// </summary>
        internal void ApplyMonitorDpi(int targetDpi)
        {
            if (!DpiRelayout.IsUsableDpi(targetDpi) || !DpiRelayout.IsUsableDpi(BaselineDpi))
            {
                return;
            }

            if (baselineFont == null)
            {
                baselineFont = Font;
                baselineImageScalingSize = ImageScalingSize;
            }

            // 嵌套下拉不必单独下发：实测把根菜单的 Font 与 ImageScalingSize 换掉之后，子菜单
            // 与它的项跟着变（字高 27→16、图标 20→11、项高 32→22），显式递归赋值一个数都不改。
            // 这也让之后才懒加载出来的项（参考图列表在 hover 时才建）自动跟上。
            ImageScalingSize = DpiRelayout.ScaleSize(baselineImageScalingSize, targetDpi, BaselineDpi);

            var previous = scaledFont;
            scaledFont = DpiRelayout.ScaleFont(baselineFont, targetDpi, BaselineDpi);
            Font = scaledFont;

            // 换完再放：此刻菜单还没显示出来，旧字体已经不被任何项引用。基线字体属于
            // ToolStripManager，绝不由这里释放。
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        void ContextStyleMenuStrip_Disposed(object sender, EventArgs e)
        {
            if (scaledFont != null)
            {
                scaledFont.Dispose();
                scaledFont = null;
            }
        }

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
