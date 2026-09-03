using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SETUNA.Main;
using SETUNA.Main.Localization;
using SETUNA.Main.Window;

public class BaseForm : Form
{
    public static new Form ActiveForm { set; get; }

    bool ownedResourcesDisposed;

    bool dpiSyncInProgress;
    int notifiedDpi;
    MonitorSnapshot monitorSnapshot = MonitorSnapshot.Unavailable;
    DpiContext dpiContext = new DpiContext(MonitorSnapshot.Unavailable);

    /// <summary>Fonts this form created while rescaling; the designer's originals are not here.</summary>
    readonly List<Font> rescaledFonts = new List<Font>();

    public BaseForm()
    {
        SETUNA.Main.FormManager.RegisterForm(this);

        // 语言变更后要重新应用文字。订阅的是静态事件，它会一直持有 this，
        // 所以退订必须成对出现在下面的确定性释放路径上。
        Lang.LanguageChanged += Lang_LanguageChanged;

        // The exact type is already available here, before InitializeComponent, so
        // physical surfaces never enter the framework's automatic layout path.
        AutoScaleMode = Policy == DpiPolicy.PhysicalSurface
            ? AutoScaleMode.None
            : AutoScaleMode.Dpi;
    }

    /// <summary>Semantic policy for this top-level window.</summary>
    protected virtual DpiPolicy DpiPolicy => DpiPolicy.LogicalUi;

    /// <summary>Exposes the selected policy to diagnostics and policy tests.</summary>
    public DpiPolicy Policy => DpiPolicyRegistry.TryGetPolicy(GetType(), out var policy) ? policy : DpiPolicy;

    public MonitorSnapshot CurrentMonitor => monitorSnapshot;

    public DpiContext CurrentDpiContext => dpiContext;

    /// <summary>
    /// 重新可见时校正一次。系统只向可见窗口发 DPI 变化消息，所以常驻托盘期间被隐藏的
    /// 窗口会错过显示设置的变化，不能只依赖消息到达。
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible && IsHandleCreated)
        {
            SyncDpiContext(0, null);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SyncDpiContext(WindowsAPI.GetWindowDpi(Handle), null);
    }

    /// <summary>
    /// 物理表面在换档时保持自己的像素外框。
    /// <para>
    /// <c>AutoScaleMode.None</c> 只挡住控件树的缩放，挡不住窗口本身：<c>WM_DPICHANGED</c> 带着
    /// 一个建议矩形，而 <c>DefWindowProc</c> 会直接照它调用 <c>SetWindowPos</c>。实测 168→96 时
    /// 137x89 的贴图窗口变成 78x51，位图却仍是 137x89——窗口比自己的图还小；放大镜
    /// 250x265→143x151，4 倍放大看到的像素数跟着变了；辅助线窗口 24x24→14x14；
    /// <c>CaptureInfo</c> 236x131→135x75。
    /// </para>
    /// <para>
    /// <see cref="DpiChangedEventArgs"/> 的 <c>Cancel</c> **挡不住这件事**（实测设了照样变）：
    /// 框架先 <c>DefWndProc</c> 再触发 <see cref="OnDpiChanged"/>，等事件能被取消时窗口已经
    /// 被改过了，那个标志管的是之后的托管缩放。所以在这里前后夹一次：进来时记下外框，
    /// <c>base.WndProc</c> 之后如果被改动就写回去。两次 <c>SetWindowPos</c> 之间没有消息泵，
    /// 中间尺寸不会被画出来。
    /// </para>
    /// <para>
    /// 位置连同尺寸一起写回：窗口跨屏是用户拖过去的，建议矩形里的位置是按倍率折算的结果，
    /// 而物理表面的语义就是「像素不动」。
    /// </para>
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_DPICHANGED = 0x02E0;

        if (m.Msg != WM_DPICHANGED || Policy != DpiPolicy.PhysicalSurface)
        {
            base.WndProc(ref m);
            return;
        }

        var keep = Bounds;

        base.WndProc(ref m);

        if (!IsDisposed && !Disposing && Bounds != keep)
        {
            Bounds = keep;
        }
    }

    /// <summary>
    /// 换档时的分工：逻辑窗体的重排交给框架，物理表面只更新显示器派生的量。
    /// <para>
    /// 物理表面的外框由 <see cref="WndProc"/> 挡住；这里两条路都要刷新显示器快照并通知子类，
    /// 因为要更新的是显示器归属和自绘装饰，不是像素尺寸。
    /// </para>
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        if (Policy == DpiPolicy.PhysicalSurface)
        {
            base.OnDpiChanged(e);
            SyncDpiContext(e.DeviceDpiNew, null);
            return;
        }

        // 必须在框架缩放之前拍下来，之后就分不清哪个容器被跳过了。比较发生在
        // CompensateSkippedContainersWhenLayoutSettles 的 BeginInvoke 里，那时框架
        // 已经缩放完毕。
        var beforeFrameworkScale = SnapshotContainerBounds();

        base.OnDpiChanged(e);
        SyncDpiContext(e.DeviceDpiNew, beforeFrameworkScale);
    }

    /// <summary>
    /// 把本窗体的显示器快照对齐到当前状态，并且只在 DPI 真的换了档时通知子类一次。
    /// <para>
    /// 三条入口共用这一段：句柄创建（含重建，建立初值）、框架的 <c>DpiChanged</c>、以及
    /// 重新可见（补上隐藏期间错过的换档）。建立初值不算换档——框架已经按当前 DPI 排过版，
    /// 这时叫子类「校正」只会让还没准备好的缓存（例如尚未抓取的预览背景）提前跑一遍。
    /// 每次换档因此最多通知一次，重复的可见/句柄事件读到同一个 DPI 就什么都不做。
    /// </para>
    /// <para>
    /// 重入保护是必需的：<see cref="OnDpiContextChanged"/> 里改控件尺寸会触发布局，布局
    /// 又可能把窗口推到另一块显示器上再送一次消息。
    /// </para>
    /// </summary>
    void SyncDpiContext(int eventDpi, Dictionary<Control, Rectangle> beforeFrameworkScale)
    {
        if (dpiSyncInProgress || IsDisposed || Disposing)
        {
            return;
        }

        dpiSyncInProgress = true;
        try
        {
            RefreshDpiContext(eventDpi);

            var currentDpi = dpiContext.DpiX;
            if (!DpiContext.IsUsableDpi(currentDpi))
            {
                // 查不到 DPI 时保留上一次通知过的值，否则下一次成功查询会被当成换档。
                return;
            }

            var changedDpi = notifiedDpi != 0 && notifiedDpi != currentDpi;
            var previousDpi = notifiedDpi;
            notifiedDpi = currentDpi;

            if (changedDpi)
            {
                OnDpiContextChanged(previousDpi);
                Invalidate(true);
                CompensateSkippedContainersWhenLayoutSettles(beforeFrameworkScale, (double)currentDpi / previousDpi);
            }
        }
        finally
        {
            dpiSyncInProgress = false;
        }
    }

    void RefreshDpiContext(int eventDpi)
    {
        var snapshot = IsHandleCreated
            ? WindowsAPI.GetMonitorSnapshotForWindow(Handle)
            : MonitorSnapshot.Unavailable;

        if (snapshot.IsAvailable)
        {
            // 有快照时几何一定用快照，但 DPI 以 WinForms 送来的事件值为准：那是本次换档
            // 里框架用来排版的数值，而 GetDpiForWindow 是另一次独立查询，快速连续换屏时
            // 两者可能短暂不一致。生产环境下它们相等，所以这只是把「谁说了算」写死。
            if (DpiContext.IsUsableDpi(eventDpi) && eventDpi != snapshot.DpiX)
            {
                snapshot = new MonitorSnapshot(
                    snapshot.Handle,
                    snapshot.DeviceName,
                    snapshot.NativeBounds,
                    snapshot.WorkingArea,
                    eventDpi,
                    eventDpi,
                    snapshot.IsPrimary);
            }

            monitorSnapshot = snapshot;
            dpiContext = new DpiContext(snapshot);
            return;
        }

        // Do not infer a monitor from DeviceDpi. The event DPI is an explicit
        // WinForms value, so retain it only as a context without fake bounds.
        if (eventDpi > 0)
        {
            dpiContext = DpiContext.FromDpi(eventDpi, eventDpi);
        }
        else
        {
            dpiContext = new DpiContext(MonitorSnapshot.Unavailable);
        }

        monitorSnapshot = MonitorSnapshot.Unavailable;
    }

    /// <summary>
    /// 补上框架漏掉的那几个容器。<c>WM_DPICHANGED</c> 的缩放遍历有时会把嵌套的
    /// <see cref="ContainerControl"/> 交给它自己缩放，而一个控件自己缩放不会改动自己的
    /// 外框，于是它的位置与尺寸留在旧 DPI 上：实测 <c>OptionForm</c> 未选中标签页里的两个
    /// <c>HotkeyControl</c> 跨 168→96 恒为 656x80，同一页里的 <c>NumericUpDown</c> 宽度也
    /// 留在旧档，而普通面板上的 <c>NumericUpDown</c> 却缩得好好的。
    /// <para>
    /// 判据是实测而不是类型规则：只有外框一点没动的容器才补。按类型一律补会把已经缩好的
    /// 容器缩第二遍——实测 <c>OpacityStyleItemPanel</c> 的 <c>numOpacity</c> 会从 84 宽变成
    /// 27 宽（0.571 的平方）。
    /// </para>
    /// <para>
    /// 而且必须等布局跑完再看：框架对选中标签页里控件的缩放是排进布局队列的，在
    /// <c>OnDpiChanged</c> 里当场量还看不到，量到的「没动」是假的。因此这一步用
    /// <see cref="Control.BeginInvoke(Delegate)"/> 推到当前消息之后。
    /// </para>
    /// <para>
    /// 这不是恢复被删掉的通用重排：只改写这一类后代的 <see cref="Control.Bounds"/>，它们
    /// 内部的排版仍由各自的自动缩放负责，其余控件一个都不碰。
    /// </para>
    /// </summary>
    void CompensateSkippedContainersWhenLayoutSettles(Dictionary<Control, Rectangle> before, double ratio)
    {
        if (before == null || ratio == 1d || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            foreach (var pair in before)
            {
                var container = pair.Key;
                var was = pair.Value;

                // 位置和宽度是判据，高度不是：跟着字体走的容器（单行编辑框、
                // NumericUpDown、组合框）会自己改高度，拿高度当判据会把它们误判成
                // 「已经缩过」——实测 numSelectAreaTrans 的高度从 34 变成 23，而
                // 位置和宽度一动没动。
                if (container.IsDisposed || container.Location != was.Location || container.Width != was.Width)
                {
                    continue;
                }

                container.SetBounds(
                    DpiContext.Scale(was.Left, ratio),
                    DpiContext.Scale(was.Top, ratio),
                    DpiContext.Scale(was.Width, ratio),
                    container.Height == was.Height ? DpiContext.Scale(was.Height, ratio) : container.Height);
            }
        }));
    }

    /// <summary>
    /// 换档前每个嵌套容器的外框，用来在换档后判断谁被跳过了。尺寸不由自己决定的控件不
    /// 记录：停靠的控件、<c>TabPage</c>（外框由 <c>TabControl</c> 算）和 <c>AutoSize</c>
    /// 的控件，改写它们会和布局引擎打架。
    /// </summary>
    Dictionary<Control, Rectangle> SnapshotContainerBounds()
    {
        var snapshot = new Dictionary<Control, Rectangle>();
        Collect(this, snapshot);

        return snapshot;

        static void Collect(Control parent, Dictionary<Control, Rectangle> into)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is ContainerControl
                    && child.Dock == DockStyle.None
                    && !child.AutoSize
                    && !(child is TabPage))
                {
                    into[child] = child.Bounds;
                }

                Collect(child, into);
            }
        }
    }

    /// <summary>
    /// 关掉标题栏上的按钮。**必须在 <c>InitializeComponent</c> 之后调用，不能写在设计器里。**
    /// <para>
    /// 窗口只有在 <c>Text</c> 非空或 <see cref="Form.ControlBox"/> 为真时才带
    /// <c>WS_CAPTION</c>。设计器按字母序生成属性赋值，<c>Text</c> 排在
    /// <c>ControlBox</c> 和 <c>FormBorderStyle</c> 之后，所以设计器里的
    /// <c>ControlBox = false</c> 会让「无标题栏、1 像素边框」这个样式短暂成立，而
    /// <c>FormBorderStyle</c> 的赋值正好在这时按它把 <see cref="Form.Size"/> 记成
    /// 客户区 + (2,2)。后面的 <c>Text</c> 赋值只改窗口文字、不重算 <c>Size</c>，
    /// 于是标题栏和边框的厚度是从客户区里扣出来的。
    /// </para>
    /// <para>
    /// 自动缩放会把这个误差乘上倍率：框架缩放的是「<c>Size</c> 减去当前边框」那一部分，
    /// 而这里它等于「客户区减去边框再加 2」。实测 168 DPI 下 <c>ScrapPaintPenTool</c>
    /// 的 57px 客户区高度变成 0（57-62 截断到 0），<c>LayerRenameWindow</c> 得到 382×42
    /// 而不是 420×151——按钮底边 129 落在客户区之外，任何缩放比例下都被裁掉一截。
    /// 96 DPI 下同样成立（客户区 236×59），所以这不是跨显示器缺陷，只是被它放大了。
    /// </para>
    /// <para>
    /// 推到构造之后就没有这个窗口期：整个 <c>InitializeComponent</c> 里 <c>ControlBox</c>
    /// 都是真，样式一直带标题栏，<c>Size</c> 因此按真实边框记账；此时再关按钮只去掉
    /// <c>WS_SYSMENU</c>，边框厚度不变。
    /// </para>
    /// </summary>
    protected void HideControlBoxAfterInitialize()
    {
        ControlBox = false;
    }

    /// <summary>
    /// 把点名列出的控件自己持有的字体按 DPI 之比换掉。
    /// <para>
    /// 框架只缩放窗体的环境字体。控件在设计器里显式指定的字体不在继承链上，跨屏之后会
    /// 留在旧档的像素大小——实测 <c>HotkeyMsg.lblKey</c> 恒为 24px，而同一个窗体里继承
    /// 字体的控件从 16px 变到 27px。
    /// </para>
    /// <para>
    /// 要求调用方点名列出控件，不做遍历：哪个控件自带字体是窗体自己的知识，而被删掉的
    /// 手工管线正是因为要靠遍历猜「这个字体归谁」才难以维护。
    /// </para>
    /// </summary>
    protected void RescaleOwnedFonts(int previousDpi, params Control[] controls)
    {
        if (controls == null)
        {
            return;
        }

        foreach (var control in controls)
        {
            if (control == null || control.IsDisposed || InheritsItsFont(control))
            {
                continue;
            }

            control.Font = RescaleOwnedFont(previousDpi, control.Font);
        }
    }

    /// <summary>
    /// 没有自己设过字体的控件读到的是父容器那一份，而那一份框架已经缩过了。再缩一次就是
    /// 平方——实测 <c>OptionForm</c> 五个导航标签会从 27px 变成 9px 而不是 15px。
    /// <para>
    /// 继承时 <see cref="Control.Font"/> 返回的就是父容器那个对象本身，所以引用相等即可
    /// 判定，不需要反射去问「这个属性被显式赋过值吗」。
    /// </para>
    /// </summary>
    static bool InheritsItsFont(Control control)
    {
        return control.Parent != null && ReferenceEquals(control.Font, control.Parent.Font);
    }

    /// <summary>
    /// 同上，但用于不在 <see cref="Control.Font"/> 上的字体属性（例如
    /// <c>StyleItemListBox.HelpFont</c>）。返回的字体由本窗体释放；DPI 没变或不可用时
    /// 原样返回，此时调用方不得释放它。
    /// </summary>
    protected Font RescaleOwnedFont(int previousDpi, Font font)
    {
        var currentDpi = dpiContext.DpiX;
        if (font == null
            || !DpiContext.IsUsableDpi(currentDpi)
            || !DpiContext.IsUsableDpi(previousDpi)
            || currentDpi == previousDpi)
        {
            return font;
        }

        var scaled = DpiContext.ScaleFont(font, currentDpi, previousDpi);
        if (ReferenceEquals(scaled, font))
        {
            return font;
        }

        // 只释放本方法自己造过的那一份。设计器造的原件不归这里管：它可能还挂在
        // 别的控件上，而这里看不到那些控件。
        if (rescaledFonts.Remove(font))
        {
            font.Dispose();
        }

        rescaledFonts.Add(scaled);

        return scaled;
    }

    /// <summary>
    /// DPI 换档后的一次校正机会：只更新自绘缓存、显示器派生的量和重绘，控件树的重排由
    /// 框架负责。新值从 <see cref="CurrentDpiContext"/> 与 <see cref="CurrentMonitor"/> 读，
    /// <paramref name="previousDpi"/> 是换档前那一档，供 <see cref="RescaleOwnedFonts"/>
    /// 与其他按两档之比换算的量使用。
    /// <para>
    /// 不带 <see cref="DpiChangedEventArgs"/>，是为了让隐藏期间错过换档的窗口走同一条
    /// 路——那条路上没有事件对象可以构造，而它的两个字段（新 DPI、建议矩形）一个已经在
    /// 上下文里，另一个属于框架。
    /// </para>
    /// </summary>
    protected virtual void OnDpiContextChanged(int previousDpi)
    {
    }

    /// <summary>
    /// 单一的确定性释放入口。子类重写 <see cref="DisposeOwnedResources"/> 释放自己持有的资源，
    /// 因此不需要各自重写 <c>Dispose(bool)</c>——设计器生成的 <c>Dispose(bool)</c> 会通过
    /// <c>base.Dispose(disposing)</c> 链到这里，无需改动生成文件。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        // Component.Dispose() 不做防重入，Close() 后再 Dispose() 会走两遍这条路径，
        // 所以钩子必须自己保证只执行一次，否则会重复释放位图和画笔。
        if (disposing && !ownedResourcesDisposed)
        {
            ownedResourcesDisposed = true;

            // 与构造函数里的订阅成对。漏掉这一句，每个开过的窗体都会被静态事件
            // 永久留住，而且已释放的窗体收到回调后操作控件会抛异常。
            Lang.LanguageChanged -= Lang_LanguageChanged;

            // RescaleOwnedFonts 造出来的字体归本窗体所有；设计器造的原件不在这里。
            foreach (var font in rescaledFonts)
            {
                font.Dispose();
            }

            rescaledFonts.Clear();

            SETUNA.Main.FormManager.DeregisterForm(this);
            DisposeOwnedResources();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 释放本窗体持有的托管资源（图像、画笔、定时器等）。
    /// 只在确定性释放路径上被调用，因此可以安全地操作托管对象和 UI。
    /// </summary>
    protected virtual void DisposeOwnedResources()
    {
    }

    /// <summary>
    /// 首次显示前应用一次当前语言。
    /// <para>
    /// 放在 <c>OnLoad</c> 而不是 <c>OnHandleCreated</c>（那时子控件可能还没建好）或
    /// <c>Shown</c>（那时窗体已经可见，用户会看到文字跳变一次）。
    /// </para>
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        ApplyLanguage();
        base.OnLoad(e);
    }

    /// <summary>
    /// 把当前语言应用到本窗体。
    /// <para>
    /// 默认实现覆盖设计器拥有的控件文字。文字由代码拼接的窗体重写本方法，先调用
    /// <c>base.ApplyLanguage()</c> 再补自己那部分——那些值不在控件树里，应用器看不到。
    /// </para>
    /// </summary>
    protected virtual void ApplyLanguage()
    {
        LocalizationApplier.Apply(this);
    }

    void Lang_LanguageChanged(object sender, EventArgs e)
    {
        // 事件可能在窗体正在释放时到达。IsDisposed 之后碰控件会抛
        // ObjectDisposedException，直接跳过。
        if (IsDisposed || Disposing)
        {
            return;
        }

        ApplyLanguage();
    }
}
