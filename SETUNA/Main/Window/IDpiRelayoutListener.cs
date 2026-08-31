namespace SETUNA.Main.Window
{
    /// <summary>
    /// 控件自己持有、参与排版而又不在 <c>Control.Font</c> / <c>Control.Bounds</c> 里的量，
    /// 由控件自己在 DPI 变化时换算。
    /// <para>
    /// <c>BaseForm</c> 的重排能换算控件树里的坐标、尺寸与字体（含控件自己持有的
    /// <c>Control.Font</c>），但看不到别的地方存的字体或像素——例如
    /// <see cref="StyleItemListBox.HelpFont"/> 是一个独立的字体属性，设计器给它设的是另一个
    /// 字族与字号，重排换不到它，跨显示器后说明文字就会停在旧 DPI 的大小上。实现本接口的
    /// 控件会在重排过程中收到通知，自己把这些量换算过去。
    /// </para>
    /// </summary>
    public interface IDpiRelayoutListener
    {
        /// <summary>
        /// 所在窗体的排版从 <paramref name="oldDpi"/> 换到了 <paramref name="newDpi"/>。
        /// 调用发生在窗体与控件树的字体、坐标都已换算之后，因此可以直接参照当前的
        /// <c>Font</c> 与 <c>Bounds</c>。
        /// </summary>
        void OnDpiRelayout(int newDpi, int oldDpi);
    }
}
