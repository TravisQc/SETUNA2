using System;
using System.Windows.Forms;
using SETUNA.Main.Localization;

public class BaseForm : Form
{
    public static new Form ActiveForm { set; get; }

    bool ownedResourcesDisposed;


    public BaseForm()
    {
        SETUNA.Main.FormManager.RegisterForm(this);

        // 语言变更后要重新应用文字。订阅的是静态事件，它会一直持有 this，
        // 所以退订必须成对出现在下面的确定性释放路径上。
        Lang.LanguageChanged += Lang_LanguageChanged;
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
