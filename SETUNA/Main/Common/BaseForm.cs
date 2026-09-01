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

    /// <summary>
    /// 框架给窗体排版时用的 DPI，也就是进程启动时的系统 DPI。
    /// <para>
    /// 不能用窗口当前所在显示器的 DPI 作为重排基准：窗体建好时的排版对应的是系统 DPI
    /// （环境字体的像素高度由系统 DPI 决定，<c>AutoScaleMode.Font</c> 的倍率又由环境字体
    /// 决定）。如果启动时窗口就落在另一块缩放不同的显示器上，把当前显示器的 DPI 当成基准
    /// 会得出「无需重排」，窗口就一直停在错误的排版上。
    /// </para>
    /// <para>
    /// 只取一次：系统 DPI 在会话中途可以被改（用户改主显示器的缩放比例），但已经建好的
    /// 窗体排版仍停在启动时那个值上，跟着变会把基准算错。
    /// </para>
    /// </summary>
    static readonly int LayoutBaselineDpi = WindowsAPI.GetSystemDpi();

    /// <summary>当前排版对应的 DPI。</summary>
    int layoutDpi = LayoutBaselineDpi;

    /// <summary>重排期间为真，用于阻止嵌套重排。</summary>
    bool relayoutInProgress;

    /// <summary>上一次重排创建的字体，下一次重排替换掉它们之后释放。</summary>
    readonly List<Font> relayoutFonts = new List<Font>();

    /// <summary>
    /// 基线排版：<see cref="LayoutBaselineDpi"/> 那一档的窗体字体、客户区、自动缩放尺度，
    /// 以及每个控件的矩形。第一次重排之前拍下。
    /// <para>
    /// 为 <c>null</c> 表示这次重排走按新旧 DPI 之比缩放当前状态的老做法：可以被用户改大小的
    /// 窗口一直如此（见 <see cref="ReproducesLayoutFromBaseline"/>），其余窗口只在拍下之前
    /// 如此。
    /// </para>
    /// </summary>
    LayoutBaseline baseline;


    public BaseForm()
    {
        SETUNA.Main.FormManager.RegisterForm(this);

        // 语言变更后要重新应用文字。订阅的是静态事件，它会一直持有 this，
        // 所以退订必须成对出现在下面的确定性释放路径上。
        Lang.LanguageChanged += Lang_LanguageChanged;
    }

    /// <summary>
    /// 本窗体是否随所处显示器的 DPI 重排。默认不参与，窗体各自重写以加入。
    /// <para>
    /// 默认值是刻意选的：漏掉一个对话框，后果是它保持原有行为；漏掉一个以像素为语义的窗口
    /// （贴图窗口、截图覆盖层、放大镜、以图像本身为画布的窗口），后果是图像被重新拉伸、
    /// 取样几何错位。风险朝安全的一侧倒。
    /// </para>
    /// </summary>
    protected virtual bool ScalesWithMonitorDpi => false;

    /// <summary>
    /// 本窗体的排版能否从启动那一档的快照重算。
    /// <para>
    /// 判据是窗口能不能被用户改大小。不能改的窗口，排版完全由「设计值 + DPI」决定，于是每次都
    /// 从同一份快照算到目标 DPI，同一个 DPI 就永远得到同一套排版，回到启动那一档更是逐像素还原
    /// 设计值。能改大小的窗口不行：快照描述的是「启动那一档 DPI 加当时那个尺寸」，照它重算会把
    /// 用户的尺寸抹掉——主窗口可以拖大拖小，尺寸还会存进选项里——那种窗口只能按新旧 DPI 之比
    /// 缩放当前状态，也就是本来的做法。
    /// </para>
    /// </summary>
    protected virtual bool ReproducesLayoutFromBaseline =>
        FormBorderStyle != FormBorderStyle.Sizable
        && FormBorderStyle != FormBorderStyle.SizableToolWindow;

    /// <summary>
    /// 拦下 <see cref="DpiRelayout.WM_DPICHANGED"/> 自己重排。
    /// <para>
    /// 不能靠 <c>Form.DpiChanged</c>：那套事件要由应用配置文件里的
    /// <c>System.Windows.Forms.ApplicationConfigurationSection</c> 打开，而单文件分发不允许
    /// 存在配置文件，实测 <c>DpiHelper.enableHighDpi</c> 为 false，事件永不触发。消息本身
    /// 是系统发的，与框架开关无关，所以只能在这里接。
    /// </para>
    /// <para>
    /// 先让基类走完默认处理再重排：默认处理不会改客户区尺寸（实测合成一条
    /// <c>WM_DPICHANGED</c> 后窗口尺寸纹丝不动），但它负责非客户区的记账，而重排施加的
    /// 目标矩形必须是最后生效的那个。
    /// </para>
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg != DpiRelayout.WM_DPICHANGED || !ScalesWithMonitorDpi || relayoutInProgress)
        {
            base.WndProc(ref m);
            return;
        }

        // 在默认处理之前把消息参数读出来，不依赖 lParam 在之后仍然有效。
        var newDpi = DpiRelayout.DpiFromMessage(m.WParam);
        var suggested = ReadSuggestedBounds(m.LParam);

        base.WndProc(ref m);

        RelayoutForDpi(newDpi, suggested);
    }

    /// <summary>
    /// 重新可见时校正一次排版。系统只向可见窗口发 DPI 变化消息，所以常驻托盘期间被隐藏的
    /// 窗口会错过显示设置的变化，不能只依赖消息到达。
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible && ScalesWithMonitorDpi && IsHandleCreated)
        {
            // 没有建议矩形，位置保持不动。
            RelayoutForDpi(WindowsAPI.GetWindowDpi(Handle), Rectangle.Empty);
        }
    }

    /// <summary>
    /// 把窗体重排到 <paramref name="newDpi"/>。DPI 没有实际变化或取不到时什么都不做。
    /// </summary>
    void RelayoutForDpi(int newDpi, Rectangle suggested)
    {
        if (!DpiRelayout.RequiresRelayout(newDpi, layoutDpi))
        {
            return;
        }

        // 基线只有在当前排版就是基线排版时才拍得准，而第一次重排之前必然如此：layoutDpi 的
        // 初值就是 LayoutBaselineDpi。放在这里而不是构造函数或 OnLoad：那两处 ApplyLanguage
        // 还没跑完，AutoSize 控件的宽度尚未定型。
        if (baseline == null && layoutDpi == LayoutBaselineDpi && ReproducesLayoutFromBaseline)
        {
            baseline = CaptureBaseline();
        }

        var oldDpi = layoutDpi;
        var previousMinimum = MinimumSize;
        var previousMaximum = MaximumSize;
        var previousFonts = relayoutFonts.ToArray();

        relayoutInProgress = true;
        try
        {
            // 重排后的客户区由这两个量算出，见下面施加处的说明。有基线时取基线那一档的值，
            // 于是客户区也变成「基线 → 目标」的一步换算。
            var clientBefore = baseline == null ? ClientSize : baseline.ClientSize;
            var scaleBefore = baseline == null ? CurrentAutoScaleDimensions : baseline.AutoScaleDimensions;

            // 旧 DPI 下的边界值会直接卡住缩放：最小尺寸挡住缩小、最大尺寸挡住放大。
            // 先解除，重排完再按新 DPI 施加。
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;

            // 自己持有字体的控件不继承窗体字体，换窗体字体带不动它们，必须单独换算。
            // 先取快照，与下面换字体的顺序无关。
            var ownFonts = CollectControlsOwningTheirFont(this);

            relayoutFonts.Clear();

            // 重排的触发点：换窗体字体，AutoScaleMode.Font 会据此把子控件的坐标、尺寸
            // 和窗体客户区一并重排（含 Anchor / Dock 与嵌套容器）。
            var fontFrom = Font;
            var fontFromDpi = oldDpi;

            if (baseline != null)
            {
                RewindToBaseline();
                fontFrom = baseline.Font;
                fontFromDpi = LayoutBaselineDpi;
            }

            Font = TrackFont(DpiRelayout.ScaleFont(fontFrom, newDpi, fontFromDpi));

            foreach (var control in ownFonts)
            {
                // 这些字体保持相对换算：字号的换算是精确可逆的（实测 9pt → 5.142857pt → 9pt
                // 往返五次分毫不差），而拿基线字体覆盖回去会把运行期改过的样式一起退回去——
                // 选项窗体的导航标签就会按选中项派生加粗版本。
                control.Font = TrackFont(DpiRelayout.ScaleFont(control.Font, newDpi, oldDpi));
            }

            // 自己持有排版状态的控件（字体属性、固定像素）在这里跟上。放在字体与坐标都换算完
            // 之后，实现方可以直接参照控件当前的 Font 与 Bounds。
            NotifyDpiRelayoutListeners(this, newDpi, oldDpi);

            // 窗体自己持有的那些量紧随其后，理由与上面同一条。
            OnDpiRelayout(newDpi, oldDpi);

            ApplySizeBounds(newDpi, oldDpi, previousMinimum, previousMaximum);

            // 客户区取「重排前的客户区 × 框架自己报告的自动缩放尺度之比」，外框再由系统按新 DPI
            // 反算。两步都不能省：
            // 框架算出的外框是客户区加上换算前的非客户区厚度，而标题栏与边框此时已经按新 DPI
            // 重绘，差额会全部落到客户区上；而 ClientSize 的 setter 也不行，它内部用的是系统 DPI
            // 的非客户区厚度（详见 WindowsAPI.GetOuterSizeForClientSize）。实测从 168 DPI 跨到
            // 96 DPI，两条弯路都给出客户区 417，而该 DPI 下的原生排版是 400，对话框底部空出一条。
            var client = DpiRelayout.ScaleClientSize(clientBefore, scaleBefore, CurrentAutoScaleDimensions);

            // 反算外框用消息带来的 newDpi，不用 GetDpiForWindow：后者在重排过程中会变。重排把
            // 窗口缩小，小窗口的重心可能因此退回原来那块显示器，于是查到的又是旧 DPI——实测
            // 重命名图层那个小对话框在 enter 时窗口 DPI 是 96、算外框时已经变回 168，按 168 的
            // 标题栏算出的外框比该有的高 17 像素，客户区随之多出一截，往返之后误差还会累积。
            var outer = client.IsEmpty
                ? Size.Empty
                : WindowsAPI.GetOuterSizeForClientSize(Handle, client, newDpi);

            var target = DpiRelayout.Compose(suggested, Bounds, outer.IsEmpty ? Size : outer);
            SetBounds(target.X, target.Y, target.Width, target.Height, BoundsSpecified.All);

            layoutDpi = newDpi;
        }
        finally
        {
            relayoutInProgress = false;
        }

        // 只释放上一次重排自己创建的字体，而且必须在全部替换完成之后：设计器与框架默认
        // 字体不在这个列表里，绝不会被误放。
        DisposeUnreferencedFonts(previousFonts);
    }

    /// <summary>
    /// 基线排版的快照。
    /// <para>
    /// 为什么要留一份：<c>AutoScaleMode.Font</c> 把子控件矩形按倍率缩放之后要取整，而取整不可逆。
    /// 168 DPI 下宽 179 的「确定」按钮缩到 96 DPI 是 98，再放回 168 就成了 180。实测跨一次边界
    /// 回来有几个控件偏 1 像素（btnOK 宽 179→180、pictureBox1 X −120→−119 宽 487→488、
    /// label1 X 430→431），之后稳定、不再累积。只要每次都从同一份基线算到目标 DPI，同一个 DPI
    /// 就永远得到同一套排版；回到基线那一档更是逐像素还原设计值。
    /// </para>
    /// <para>
    /// 这与 <c>Mainform</c> 重写 <see cref="ApplySizeBounds"/> 的理由是同一个——那里也是按基线
    /// 重算而不是缩放上一次的值，只不过主窗口的尺寸边界有现成的实测基线可用。
    /// </para>
    /// </summary>
    sealed class LayoutBaseline
    {
        /// <summary>基线那一档的窗体字体。属于设计器，绝不由重排释放。</summary>
        public Font Font;

        public Size ClientSize;

        /// <summary>
        /// 基线那一档框架报告的自动缩放尺度。客户区的换算要用它作分母，见
        /// <see cref="DpiRelayout.ScaleClientSize"/>。
        /// </summary>
        public SizeF AutoScaleDimensions;

        public readonly List<Entry> Entries = new List<Entry>();

        /// <summary>一个控件在基线那一档的矩形。</summary>
        public struct Entry
        {
            public Control Control;
            public Rectangle Bounds;
        }
    }

    /// <summary>
    /// 拍下当前排版作为基线。只在当前排版确实是基线排版时调用。
    /// <para>
    /// 条目按先父后子的顺序排，还原时照这个顺序写回去，父容器的尺寸先落定。
    /// </para>
    /// </summary>
    LayoutBaseline CaptureBaseline()
    {
        var captured = new LayoutBaseline
        {
            Font = Font,
            ClientSize = ClientSize,
            AutoScaleDimensions = CurrentAutoScaleDimensions
        };

        CaptureBounds(this, captured);

        return captured;
    }

    static void CaptureBounds(Control parent, LayoutBaseline into)
    {
        foreach (Control child in parent.Controls)
        {
            into.Entries.Add(new LayoutBaseline.Entry { Control = child, Bounds = child.Bounds });
            CaptureBounds(child, into);
        }
    }

    /// <summary>
    /// 把窗体退回基线排版，为紧接着的「基线 → 目标 DPI」那一步做准备。
    /// <para>
    /// 三步的顺序都不能动。先换字体：这一下会顺手按倍率重排一遍（带取整损失），随后写回的
    /// 矩形让那一遍作废，而框架的 <c>AutoScaleDimensions</c> 因此正好停在基线那一档，于是
    /// 下面换成目标字号时倍率就是「基线 → 目标」，整个重排只取整一次。
    /// </para>
    /// <para>
    /// 再把客户区退回基线值，最后才写子控件的矩形。<c>Anchor</c> 的偏移量是控件矩形被写入
    /// 时按父容器当时的尺寸算出来、然后缓存下来的，父容器还停在上一档尺寸上就会把偏移量写坏，
    /// 之后无论怎么调整都回不来：实测样式编辑窗体的 groupBox2 因此宽出 15 像素、高出 44 像素，
    /// 确定按钮下移 44 像素，而且第二次往返仍是同样的偏差。<c>Dock</c> 的控件不受影响——它们
    /// 的矩形完全由父容器推出来，不缓存任何偏移量。
    /// </para>
    /// <para>
    /// 跳过已释放的控件：快照持有的是控件引用，窗体理论上可以在运行期换掉自己的控件。参与
    /// 重排的几个窗体眼下都不这么做（控件树在 <c>InitializeComponent</c> 之后就固定了），
    /// 真出现了也只是那个控件退回相对换算，不会抛异常。
    /// </para>
    /// </summary>
    void RewindToBaseline()
    {
        Font = baseline.Font;

        RewindClientSize();

        foreach (var entry in baseline.Entries)
        {
            if (!entry.Control.IsDisposed)
            {
                entry.Control.Bounds = entry.Bounds;
            }
        }
    }

    /// <summary>
    /// 把客户区退回基线尺寸。
    /// <para>
    /// 非客户区厚度就地量：<c>Size</c> 减 <c>ClientSize</c>，两个值同一刻从框架读出，必然自洽。
    /// 不按 DPI 反算（<see cref="WindowsAPI.GetOuterSizeForClientSize"/>），也不设完再读回来补差额：
    /// 重排是在一条消息里做完的，边框换到新 DPI 的确切时机不由这里决定，两种做法都在猜，实测
    /// 登录输入框会差 25 像素、而且往返之间时好时坏。边框厚度与客户区大小无关，量一次就够。
    /// </para>
    /// </summary>
    void RewindClientSize()
    {
        var frame = new Size(Width - ClientSize.Width, Height - ClientSize.Height);

        SetBounds(
            0,
            0,
            baseline.ClientSize.Width + frame.Width,
            baseline.ClientSize.Height + frame.Height,
            BoundsSpecified.Size);
    }

    /// <summary>
    /// 窗体自己持有、参与排版而又不在控件树里的量，由子类在这里换算。
    /// <para>
    /// 与 <see cref="IDpiRelayoutListener"/> 是同一件事的两个入口：那个接口给控件用，本方法给
    /// 窗体用。<see cref="NotifyDpiRelayoutListeners"/> 只遍历子控件，窗体自己拿不到那个回调，
    /// 而窗体持有的这类量并不少见——样式设置对话框预览框里那张按预览框尺寸抓下来的背景位图就是
    /// 一例，预览框随 DPI 变大之后它不跟上，右下就会露出一条没画到的空白。
    /// </para>
    /// <para>
    /// 调用时机与控件那一路相同：字体与坐标都已换算完，因此可以直接参照控件当前的
    /// <c>Font</c> 与 <c>Bounds</c>。本方法在 <c>WM_DPICHANGED</c> 的处理过程中被调用，抛出
    /// 异常会让这一次重排半途而废，实现方需要自己把可能失败的操作兜住。
    /// </para>
    /// </summary>
    protected virtual void OnDpiRelayout(int newDpi, int oldDpi)
    {
    }

    /// <summary>
    /// 重排结束前按新 DPI 重新施加尺寸边界。默认按新旧 DPI 之比换算重排前的值；
    /// 有实测基线的窗体（主窗口）重写为按基线重算，以免反复跨屏累积误差。
    /// </summary>
    protected virtual void ApplySizeBounds(int newDpi, int oldDpi, Size previousMinimum, Size previousMaximum)
    {
        // 空尺寸在 WinForms 里表示「无此约束」，ScaleSize 原样返回 0，因此没有设过
        // 边界的窗体不会被凭空加上一个。
        MinimumSize = DpiRelayout.ScaleSize(previousMinimum, newDpi, oldDpi);
        MaximumSize = DpiRelayout.ScaleSize(previousMaximum, newDpi, oldDpi);
    }

    /// <summary>
    /// 从 <c>WM_DPICHANGED</c> 的 <c>lParam</c> 读建议矩形，指针为空时返回
    /// <see cref="Rectangle.Empty"/>（调用方据此保持当前位置）。
    /// <para>
    /// 直接读四个 32 位整数，不经过结构体封送：这样不依赖任何结构体的字段顺序或布局特性，
    /// 也不产生封送用的临时对象。
    /// </para>
    /// </summary>
    static Rectangle ReadSuggestedBounds(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB(
            Marshal.ReadInt32(lParam, 0),
            Marshal.ReadInt32(lParam, 4),
            Marshal.ReadInt32(lParam, 8),
            Marshal.ReadInt32(lParam, 12));
    }

    /// <summary>
    /// 收集自身被单独赋过字体、因而不继承父控件字体的控件（不含窗体自己）。
    /// <para>
    /// 判据是引用相等：继承字体时 <c>Control.Font</c> 返回的就是父控件那一个实例，自己设过
    /// 才会是别的实例。.NET Framework 里没有公开的「是否显式设过字体」查询，而
    /// <c>Control.Properties</c> 不可访问，这个判据不需要反射。
    /// </para>
    /// </summary>
    static List<Control> CollectControlsOwningTheirFont(Control root)
    {
        var owners = new List<Control>();
        Collect(root, owners);

        return owners;
    }

    static void Collect(Control parent, List<Control> owners)
    {
        foreach (Control child in parent.Controls)
        {
            if (!ReferenceEquals(child.Font, parent.Font))
            {
                owners.Add(child);
            }

            Collect(child, owners);
        }
    }

    /// <summary>
    /// 通知控件树里实现了 <see cref="IDpiRelayoutListener"/> 的控件换算它们自己持有的排版量。
    /// </summary>
    static void NotifyDpiRelayoutListeners(Control parent, int newDpi, int oldDpi)
    {
        foreach (Control child in parent.Controls)
        {
            var listener = child as IDpiRelayoutListener;
            if (listener != null)
            {
                listener.OnDpiRelayout(newDpi, oldDpi);
            }

            NotifyDpiRelayoutListeners(child, newDpi, oldDpi);
        }
    }

    Font TrackFont(Font font)    {
        relayoutFonts.Add(font);

        return font;
    }

    /// <summary>
    /// 释放上一轮重排创建、这一轮已经不再被使用的字体。
    /// <para>
    /// 逐个核对是否仍被控件树引用，而不是无条件释放：控件的字体可能被外部代码换成别的
    /// （选项窗体的导航标签就会在选中项或语言变化时用当前字体派生出加粗版本），也可能被
    /// 两个控件共用。释放一个仍在使用的字体会让下一次绘制抛异常，而漏放一个只是多占一个
    /// GDI 句柄，所以这里刻意偏向漏放：<see cref="Font"/> 的相等性是按值的，与在用字体
    /// 取值相同的实例会被一并保留。
    /// </para>
    /// </summary>
    void DisposeUnreferencedFonts(IEnumerable<Font> candidates)
    {
        var referenced = new HashSet<Font>();
        CollectFonts(this, referenced);

        foreach (var font in candidates)
        {
            if (!referenced.Contains(font))
            {
                font.Dispose();
            }
        }
    }

    static void CollectFonts(Control control, HashSet<Font> fonts)
    {
        fonts.Add(control.Font);

        foreach (Control child in control.Controls)
        {
            CollectFonts(child, fonts);
        }
    }

    static void DisposeFonts(IEnumerable<Font> fonts)
    {
        foreach (var font in fonts)
        {
            font.Dispose();
        }
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

            // 重排创建的字体是 GDI 句柄，窗体关掉就得还回去。
            DisposeFonts(relayoutFonts);
            relayoutFonts.Clear();

            // 基线快照持有整棵控件树的引用（还有设计器的字体，那个不归重排释放）。
            baseline = null;

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
