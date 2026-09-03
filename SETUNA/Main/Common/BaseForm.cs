using System;
using System.Drawing;
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
            SyncDpiContext(0);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SyncDpiContext(WindowsAPI.GetWindowDpi(Handle));
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
    /// 换档时的分工：重排整个交给框架，本类只更新显示器派生的量。
    /// <para>
    /// 物理表面的外框由 <see cref="WndProc"/> 挡住；两条路都要刷新显示器快照并通知子类，
    /// 因为要更新的是显示器归属和自绘装饰，不是像素尺寸。
    /// </para>
    /// <para>
    /// **这里一个像素、一个点值都不许按 DPI 之比再乘一遍。** 真实换屏时系统会把
    /// <c>WM_DPICHANGED_BEFOREPARENT</c> 逐个发给子窗口，框架在那里把每个子控件的矩形、
    /// 显式指定的 <see cref="Control.Font"/> 和字体派生的常量全部换算好，等这个方法被调用时
    /// 控件树已经是新一档的样子了。曾经这里有两段「补框架漏掉的」代码，都是拿 DPI 之比去乘：
    /// 详见 <see cref="OnDpiContextChanged"/>。
    /// </para>
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        SyncDpiContext(e.DeviceDpiNew);
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
    void SyncDpiContext(int eventDpi)
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
    /// DPI 换档后的一次校正机会：只更新**框架碰不到的东西**——自绘缓存、按预览框尺寸抓的
    /// 位图、显示器派生的几何——然后重绘。控件树的重排整个归框架。新值从
    /// <see cref="CurrentDpiContext"/> 与 <see cref="CurrentMonitor"/> 读。
    /// <para>
    /// **不要在这里按 <paramref name="previousDpi"/> 与当前档之比去乘任何东西。** 这条曾经
    /// 被违反过两次，两次都是因为读数取自**合成**的 <c>WM_DPICHANGED</c>：合成消息只到顶层
    /// 窗口，而真实换屏时系统还会把 <c>WM_DPICHANGED_BEFOREPARENT</c> 逐个发给子窗口，框架
    /// 在那条路上把子控件的矩形、**显式指定的 <see cref="Control.Font"/>** 和字体派生的常量
    /// 全部换算好。所以合成消息下看起来「框架漏掉的」，真机上是「框架已经做完的」，再乘一遍
    /// 就得到倍率的平方。实测（2026-09-03，真实 96↔168 双屏、真实拖拽）：
    /// <c>OptionForm</c> 的六个导航标签在 168 DPI 上变成 15.75pt/47px（应为 9pt/27px）而
    /// 标签高只有 30px，文字被裁；<c>numDustBox</c> 在 96 DPI 上缩到 26x21 并落在
    /// <c>chkDustBox</c> 底下彻底看不见；<c>hotkeyControl1</c> 反过来涨到 613x70（应为
    /// 200x23）。
    /// </para>
    /// <para>
    /// <paramref name="previousDpi"/> 因此只用来判断「换的是哪两档」，不作为换算因子。真要给
    /// 一个框架碰不到的量换档，按新 DPI **绝对**算一遍（如
    /// <c>Mainform.ApplyWindowSizeBounds</c>），或者挂到它依赖的那个量自己的变更通知上（如
    /// <c>StyleItemListBox.OnFontChanged</c> 之于 <c>HelpFont</c>）——绝对算法幂等，通知次数
    /// 多一次少一次都不会累积。
    /// </para>
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
