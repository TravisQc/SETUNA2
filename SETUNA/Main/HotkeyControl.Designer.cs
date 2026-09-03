namespace SETUNA.Main
{
    partial class HotkeyControl
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            // 与其余窗体同一套基线：96 DPI 逻辑单位 + Dpi 模式，取代原来的 Font 模式加空的
            // AutoScaleDimensions（那等于「谁也不缩放我」）。
            //
            // 注意这不能修好跨屏：父窗体的 WM_DPICHANGED 缩放会跳过嵌套的
            // ContainerControl，让它自己缩放，而它自己缩放不改变自己的外框——实测在一个
            // 确实缩放了的 OptionForm 里，168→96 之后本控件仍是 656x80。Inherit 与 Dpi
            // 两种模式都试过，结果相同。真正的修法记在任务 6.1 的说明里。
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        }

        #endregion
    }
}
