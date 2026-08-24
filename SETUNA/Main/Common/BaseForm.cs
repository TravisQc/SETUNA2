using System.Windows.Forms;

public class BaseForm : Form
{
    public static new Form ActiveForm { set; get; }

    bool ownedResourcesDisposed;


    public BaseForm()
    {
        SETUNA.Main.FormManager.RegisterForm(this);
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
}
