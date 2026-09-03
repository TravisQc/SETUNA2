using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using com.clearunit;
using SETUNA.Main;
using SETUNA.Main.Common;
using SETUNA.Main.KeyItems;
using SETUNA.Main.Localization;
using SETUNA.Main.Option;
using SETUNA.Main.Style;
using SETUNA.Main.Window;

namespace SETUNA
{
    // Token: 0x02000037 RID: 55
    public sealed partial class Mainform : BaseForm, IScrapKeyPressEventListener, IScrapAddedListener, IScrapRemovedListener, IScrapStyleListener, IScrapMenuListener, ISingletonForm
    {
        public static Mainform Instance { private set; get; }

        // Token: 0x060001EE RID: 494 RVA: 0x0000A4C4 File Offset: 0x000086C4
        public Mainform()
        {
            Instance = this;

            _isstart = false;
            _iscapture = false;
            _isoption = false;
            InitializeComponent();
            scrapBook = new ScrapBook(this);
            scrapBook.addKeyPressListener(this);
            scrapBook.addScrapAddedListener(this);
            scrapBook.addScrapRemovedListener(this);
            optSetuna = new SetunaOption();
            dustbox = new Queue<ScrapBase>();
            scrapBook.DustBox = dustbox;
            scrapBook.DustBoxCapacity = 5;
            keyBook = optSetuna.GetKeyItemBook();
            _imgpool = new List<ScrapSource>();
            SetSubMenu();

            Text = $"SETUNA {Application.ProductVersion}";
        }

        /// <summary>
        /// 主窗口的文字有两处不在控件树里：由代码拼接的标题，以及每次由
        /// <see cref="CPreStyle"/> 对象重建的托盘菜单。基类的应用器看不到它们。
        /// </summary>
        protected override void ApplyLanguage()
        {
            base.ApplyLanguage();

            Text = $"SETUNA {Application.ProductVersion}";

            // 托盘菜单本来就是清空后由预设操作对象重新构建的，那些对象的名称已改为
            // 按键查表，所以重建一次就跟上了当前语言。参考图右键菜单同理。
            SetSubMenu();
            SetScrapSubMenu();
        }

        // Token: 0x17000055 RID: 85
        // (get) Token: 0x060001F0 RID: 496 RVA: 0x0000A59C File Offset: 0x0000879C
        // (set) Token: 0x060001EF RID: 495 RVA: 0x0000A577 File Offset: 0x00008777
        public bool IsStart
        {
            get => _isstart;
            set
            {
                _isstart = value;
                if (value && _imgpool.Count > 0)
                {
                    timPool.Start();
                }
            }
        }

        // Token: 0x17000056 RID: 86
        // (get) Token: 0x060001F2 RID: 498 RVA: 0x0000A5C9 File Offset: 0x000087C9
        // (set) Token: 0x060001F1 RID: 497 RVA: 0x0000A5A4 File Offset: 0x000087A4
        public bool IsCapture
        {
            get => _iscapture;
            set
            {
                _iscapture = value;
                if (!value && _imgpool.Count > 0)
                {
                    timPool.Start();
                }
            }
        }

        // Token: 0x17000057 RID: 87
        // (get) Token: 0x060001F4 RID: 500 RVA: 0x0000A5F6 File Offset: 0x000087F6
        // (set) Token: 0x060001F3 RID: 499 RVA: 0x0000A5D1 File Offset: 0x000087D1
        public bool IsOption
        {
            get => _isoption;
            set
            {
                _isoption = value;
                if (!value && _imgpool.Count > 0)
                {
                    timPool.Start();
                }
            }
        }

        // Token: 0x060001F5 RID: 501 RVA: 0x0000A600 File Offset: 0x00008800
        private void SetSubMenu()
        {
            setunaIconMenu.Scrap = scrapBook.GetDummyScrap();
            setunaIconMenu.Items.Clear();
            setunaIconMenu.Items.Add(new CScrapListStyle().GetToolStrip(scrapBook));
            setunaIconMenu.Items.Add(new CDustBoxStyle().GetToolStrip(scrapBook));
            setunaIconMenu.Items.Add(new CDustEraseStyle().GetToolStrip());
            setunaIconMenu.Items.Add(new CDustScrapStyle().GetToolStrip());
            setunaIconMenu.Items.Add(new ToolStripSeparator());
            setunaIconMenu.Items.Add(new CCaptureStyle().GetToolStrip());
            setunaIconMenu.Items.Add(new CPasteStyle().GetToolStrip());
            setunaIconMenu.Items.Add(new ToolStripSeparator());
            setunaIconMenu.Items.Add(new CShowVersionStyle().GetToolStrip());
            setunaIconMenu.Items.Add(new COptionStyle().GetToolStrip());
            setunaIconMenu.Items.Add(new ToolStripSeparator());
            setunaIconMenu.Items.Add(new CShutDownStyle().GetToolStrip());
        }

        /// <summary>
        /// 重建参考图右键菜单。
        /// <para>
        /// 从 <see cref="OptionApply"/> 里提出来，是因为语言变更也需要重建：菜单里混着
        /// 用户自定义的自动操作（名称是用户数据，原样使用）和预设操作（名称按键查表），
        /// 后者只有重建一次才会跟上新语言。
        /// </para>
        /// </summary>
        private void SetScrapSubMenu()
        {
            // ApplyLanguage() 在 Load 事件之前跑，那时配置还没读进来，
            // SubMenuStyles/Styles 可能还是 null。菜单随后会由 OptionApply() 建好。
            if (optSetuna?.Scrap?.SubMenuStyles == null || optSetuna.Styles == null)
            {
                return;
            }

            subMenu.Items.Clear();
            foreach (var num in optSetuna.Scrap.SubMenuStyles)
            {
                if (num >= 0)
                {
                    using (var enumerator2 = optSetuna.Styles.GetEnumerator())
                    {
                        while (enumerator2.MoveNext())
                        {
                            var cstyle = enumerator2.Current;
                            if (cstyle.StyleID == num)
                            {
                                subMenu.Items.Add(cstyle.GetToolStrip(scrapBook));
                            }
                        }
                        continue;
                    }
                }
                var preStyle = CPreStyles.GetPreStyle(num);
                if (preStyle != null)
                {
                    subMenu.Items.Add(preStyle.GetToolStrip(scrapBook));
                }
            }
        }

        // Token: 0x060001F6 RID: 502 RVA: 0x0000A740 File Offset: 0x00008940
        public void StartCapture()
        {
            if (IsCapture || Mainform.cap_form == null || IsOption)
            {
                return;
            }
            try
            {
                if (frmClickCapture != null)
                {
                    frmClickCapture.Stop();
                }
                IsCapture = true;
                Console.WriteLine(string.Concat(new object[]
                {
                    "9 - ",
                    DateTime.Now.ToString(),
                    " ",
                    DateTime.Now.Millisecond
                }));
                Mainform.cap_form.OnCaptureClose = new CaptureForm.CaptureClosedDelegate(EndCapture);
                Mainform.cap_form.ShowCapture(optSetuna.Setuna);
                Console.WriteLine(string.Concat(new object[]
                {
                    "16 - ",
                    DateTime.Now.ToString(),
                    " ",
                    DateTime.Now.Millisecond
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Mainform StartCapture Exception:" + ex.Message);
                IsCapture = false;
                if (Mainform.cap_form != null)
                {
                    Mainform.cap_form.DialogResult = DialogResult.Cancel;
                }
                EndCapture(Mainform.cap_form);
            }
        }

        // Token: 0x060001F7 RID: 503 RVA: 0x0000A89C File Offset: 0x00008A9C
        private void EndCapture(CaptureForm cform)
        {
            try
            {
                Console.WriteLine("Mainform EndCapture Start---");
                if (cform.DialogResult == DialogResult.OK)
                {
                    using (var clipBitmap = cform.ClipBitmap)
                    {
                        if (clipBitmap != null)
                        {
                            scrapBook.AddScrap(clipBitmap, cform.ClipStart.X, cform.ClipStart.Y, cform.ClipSize.Width, cform.ClipSize.Height);
                        }
                    }
                }
                cform.Hide();
                Cursor.Clip = Rectangle.Empty;
                Console.WriteLine("Mainform EndCapture End---");
            }
            catch (Exception ex)
            {
                Console.WriteLine("MainForm EndCapture Exception:" + ex.Message);
            }
            finally
            {
                IsCapture = false;
                if (frmClickCapture != null)
                {
                    frmClickCapture.Restart();
                }
            }
        }

        // Token: 0x060001F8 RID: 504 RVA: 0x0000A994 File Offset: 0x00008B94
        public void Option()
        {
            if (IsCapture)
            {
                return;
            }
            IsOption = true;
            var opt = (SetunaOption)optSetuna.Clone();
            var list = new List<ScrapBase>();
            try
            {
                foreach (var scrapBase in scrapBook)
                {
                    if (scrapBase.Visible && scrapBase.TopMost)
                    {
                        list.Add(scrapBase);
                    }
                }
                foreach (var scrapBase2 in list)
                {
                    scrapBase2.TopMost = false;
                }
                base.TopMost = false;
                if (frmClickCapture != null)
                {
                    frmClickCapture.Stop();
                }
                using (var optionForm = new OptionForm(opt)
                {
                    StartPosition = FormStartPosition.CenterScreen
                })
                {
                    optionForm.ShowDialog();
                    if (optionForm.DialogResult == DialogResult.OK)
                    {
                        optSetuna = optionForm.Option;
                        OptionApply();
                        SaveOption();
                    }
                }
            }
            finally
            {
                base.TopMost = true;
                foreach (var scrapBase3 in list)
                {
                    scrapBase3.TopMost = true;
                }
                IsOption = false;
            }
        }

        // Token: 0x060001F9 RID: 505 RVA: 0x0000AB90 File Offset: 0x00008D90
        private void OptionApply()
        {
            _isApplyingOptions = true;
            try
            {
                keyBook = optSetuna.GetKeyItemBook();
                if (optSetuna.Setuna.DustBoxEnable)
                {
                    scrapBook.DustBoxCapacity = (short)optSetuna.Setuna.DustBoxCapacity;
                }
                else
                {
                    scrapBook.DustBoxCapacity = 0;
                }
                if (optSetuna.Setuna.AppType == SetunaOption.SetunaOptionData.ApplicationType.ApplicationMode)
                {
                    base.ShowInTaskbar = true;
                    setunaIcon.Visible = false;
                    base.MinimizeBox = true;
                    base.Visible = true;
                }
                else
                {
                    setunaIcon.Visible = true;
                    base.ShowInTaskbar = false;
                    base.MinimizeBox = false;
                    base.WindowState = FormWindowState.Normal;
                    base.Visible = optSetuna.Setuna.ShowMainWindow;
                }

                _hotKeysInitialized = true;
                RegisterHotKeys(true);

                SetScrapSubMenu();

                if (optSetuna.Setuna.ClickCapture)
                {
                    if (frmClickCapture == null)
                    {
                        frmClickCapture = new ClickCapture(optSetuna.Setuna.ClickCaptureValue);
                        frmClickCapture.ClickCaptureEvent += frmClickCapture_ClickCaptureEvent;
                        frmClickCapture.Show();
                    }
                    else
                    {
                        frmClickCapture.ClickFlags = optSetuna.Setuna.ClickCaptureValue;
                        frmClickCapture.Restart();
                    }
                }
                else if (frmClickCapture != null)
                {
                    frmClickCapture.Close();
                    frmClickCapture.Dispose();
                    frmClickCapture = null;
                }

                windowTimer.Enabled = optSetuna.Setuna.TopMostEnabled;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Mainform OptionApply Exception:" + ex.Message);
            }
            finally
            {
                _isApplyingOptions = false;
            }
        }

        private void RegisterHotKeys(bool showError)
        {
            // 每个热键独立处理：某个热键被其他程序占用时，其余热键照常尝试注册。
            // 注意不能用 Enum.GetValues 遍历——HotKeyID 带 __Count__ 哨兵值，
            // 那会让 RegistHotKey 里的 hkScrap[2] 越界。
            for (var hotKeyId = HotKeyID.Capture; hotKeyId < HotKeyID.__Count__; hotKeyId++)
            {
                if (optSetuna.RegistHotKey(base.Handle, hotKeyId) == HotKeyRegistrationResult.Failed)
                {
                    ReportHotKeyRegistrationFailure(hotKeyId, showError);
                }
            }
        }

        // 只上报单个热键的注册失败。以前这里会把 optSetuna.ScrapHotKeyEnable 置 false，
        // 但那是用户的设置项而不是运行时状态：截图热键被占用会连带关掉显示/隐藏热键，
        // 并且这个「关闭」还会被 SaveOption 写回配置文件，永久生效。
        private void ReportHotKeyRegistrationFailure(HotKeyID hotKeyId, bool showError)
        {
            // 启动期和句柄重建期静默：此时用户没有发起任何操作，不该弹模态对话框。
            if (!showError)
            {
                return;
            }

            using (var message = new HotkeyMsg
            {
                HotKey = optSetuna.ScrapHotKeys[(int)hotKeyId]
            })
            {
                message.ShowDialog();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_hotKeysInitialized && !_isApplyingOptions)
            {
                RegisterHotKeys(false);
            }
        }

        // Token: 0x060001FA RID: 506 RVA: 0x0000AE78 File Offset: 0x00009078
        private void frmClickCapture_ClickCaptureEvent(object sender, EventArgs e)
        {
            StartCapture();
        }

        // Token: 0x060001FB RID: 507 RVA: 0x0000AE80 File Offset: 0x00009080
        private void CloseSetuna()
        {
            base.Close();
        }

        // Token: 0x060001FC RID: 508 RVA: 0x0000AE88 File Offset: 0x00009088
        public void ScrapKeyPress(object sender, ScrapKeyPressEventArgs e)
        {
            var keyItem = keyBook.FindKeyItem(e.key);
            if (keyItem != null)
            {
                var scrapBase = (ScrapBase)sender;
                keyItem.ParentStyle.Apply(ref scrapBase);
            }
        }

        // Token: 0x060001FD RID: 509 RVA: 0x0000AEBE File Offset: 0x000090BE
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0312)
            {
                var id = (HotKeyID)m.WParam;
                switch (id)
                {
                    case HotKeyID.Capture:
                        StartCapture();
                        break;
                    case HotKeyID.Function1:
                        SetAllScrapsActive(!allScrapActive);
                        break;
                }
            }
        }

        // Token: 0x060001FE RID: 510 RVA: 0x0000AEE8 File Offset: 0x000090E8
        private void SaveOption()
        {
            var configFile = SetunaOption.ConfigFile;
            var allType = SetunaOption.GetAllType();
            try
            {
                var xmlSerializer = new XmlSerializer(optSetuna.GetType(), allType);

                // 原子写：序列化中途失败时磁盘上的旧配置保持完好。
                AtomicFile.Write(configFile, stream => xmlSerializer.Serialize(stream, optSetuna));
            }
            catch
            {
                MessageBox.Show(Lang.T("Message.ConfigSaveFailed"), "SETUNA2", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        /// <summary>
        /// 主窗口随所处显示器的 DPI 重排。
        /// </summary>
        protected override DpiPolicy DpiPolicy => DpiPolicy.LogicalUi;

        /// <summary>
        /// 按 <paramref name="dpi"/> 施加尺寸边界。两个边界都是 96 DPI 的逻辑基线，
        /// 按当前显示器的 DPI 换算，因此在每台显示器上是同一个视觉尺寸。
        /// <para>
        /// 在代码里设置使 <see cref="MainWindowGeometry"/> 成为运行时约束的唯一来源，
        /// 设计器里的字面量只服务于设计时预览。
        /// </para>
        /// </summary>
        private void ApplyWindowSizeBounds(int dpi)

        {
            var minimum = MainWindowGeometry.ScaleMinimum(dpi);
            var maximum = MainWindowGeometry.ScaleMaximum(dpi);

            // Size.Empty 表示「DPI 取不到，不应改动当前边界」。
            if (minimum.IsEmpty || maximum.IsEmpty)
            {
                return;
            }

            // 先放开上限再收下限：反过来时若新下限大于旧上限，中间状态会被旧上限钳住。
            MaximumSize = maximum;
            MinimumSize = minimum;
        }

        /// <summary>
        /// 换到另一块显示器之后重算尺寸边界。
        /// <para>
        /// 框架在缩放窗体时也会按 DPI 之比缩放 <c>MinimumSize</c>/<c>MaximumSize</c>，但那是
        /// 拿换档前的值再乘一次比例，反复跨屏会累积四舍五入误差。这两个边界有确定的基线，
        /// 直接按新 DPI 重算一遍就没有累积项，因此这里覆盖框架的结果而不是叠加。
        /// </para>
        /// </summary>
        protected override void OnDpiContextChanged(int previousDpi)
        {
            base.OnDpiContextChanged(previousDpi);
            ApplyWindowSizeBounds(CurrentDpiContext.DpiX);

            // 两个按钮在设计器里显式指定了字体（微软雅黑 9pt），不在窗体的字体继承链上。
            RescaleOwnedFonts(previousDpi, button1, button4);
        }

        /// <summary>
        /// 施加保存下来的尺寸，没有保存值时施加按当前 DPI 换算的默认客户区。
        /// <para>
        /// 默认客户区必须换算：<see cref="MainWindowGeometry"/> 的基线是 96 DPI 逻辑值，
        /// 而这里赋给 <see cref="Form.ClientSize"/> 的是物理像素，直接赋值会让 175% 下的
        /// 窗口停在设计尺寸的 57%（而里面的控件已经被框架放大过了）。
        /// </para>
        /// </summary>
        private void RestoreMainWindowSize(int dpi)
        {
            if (optSetuna == null)
            {
                ApplyDefaultClientSize(dpi);
                return;
            }

            var restored = MainWindowGeometry.RestoreWindowSize(
                optSetuna.MainWindowWidth,
                optSetuna.MainWindowHeight,
                optSetuna.MainWindowDpi,
                dpi,
                MinimumSize,
                MaximumSize,
                out var diagnostic);

            if (diagnostic != null)
            {
                Console.WriteLine(diagnostic);
            }

            if (restored.IsEmpty)
            {
                ApplyDefaultClientSize(dpi);
                return;
            }

            Size = restored;
        }

        private void ApplyDefaultClientSize(int dpi)
        {
            var client = MainWindowGeometry.ScaleDefaultClient(dpi);

            // DPI 取不到时保留框架在构造期按设计器基线排好的客户区，而不是把逻辑基线
            // 当像素写下去。
            if (!client.IsEmpty)
            {
                ClientSize = client;
            }
        }

        /// <summary>
        /// 保存当前窗口外框尺寸，连同它所属的那一档 DPI。
        /// <para>
        /// 只在关闭时调用，所以跨显示器本身不会重写保存值；而记下 DPI 之后，即使跨屏后
        /// 才关闭，写下的物理尺寸与 DPI 一起描述的仍是用户拖出来的那个视觉尺寸。
        /// </para>
        /// </summary>
        private void SaveMainWindowSize()
        {
            if (optSetuna == null)
            {
                return;
            }

            var clamped = MainWindowGeometry.Clamp(Size.Width, Size.Height, MinimumSize, MaximumSize);
            optSetuna.MainWindowWidth = clamped.Width;
            optSetuna.MainWindowHeight = clamped.Height;
            optSetuna.MainWindowDpi = MainWindowGeometry.PersistableDpi(WindowsAPI.GetWindowDpi(Handle));
        }

        // Token: 0x060001FF RID: 511 RVA: 0x0000AF58 File Offset: 0x00009158
        // 只负责读取。选项应用由 Mainform_Load 单点调用 —— 以前这里的 finally
        // 也调一次 OptionApply()，导致启动时热键注册、子菜单重建、点击截图组件
        // 创建各执行两遍。
        private void LoadOption()
        {
            var configFile = SetunaOption.ConfigFile;
            try
            {
                if (!File.Exists(configFile))
                {
                    optSetuna = SetunaOption.GetDefaultOption();
                }
                else
                {
                    var allType = SetunaOption.GetAllType();
                    var xmlSerializer = new XmlSerializer(typeof(SetunaOption), allType);
                    using (var fileStream = new FileStream(configFile, FileMode.Open, FileAccess.Read))
                    {
                        optSetuna = (SetunaOption)xmlSerializer.Deserialize(fileStream);
                    }
                }
            }
            catch
            {
                optSetuna = SetunaOption.GetDefaultOption();
                MessageBox.Show(Lang.T("Message.ConfigLoadFailed"), "SETUNA2", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                // 语言在这里生效，而不是在 Program.Main：那样得把配置文件读两遍。
                // 读失败走默认设置时 Language 为空，等同「跟随系统」，与 Lang 的初值
                // 一致，因此这条路径也不会留下错误的语言。
                Lang.SetLanguage(optSetuna.Setuna.Language);
            }
        }

        // Token: 0x06000200 RID: 512 RVA: 0x0000B000 File Offset: 0x00009200
        public void RestoreScrap(object sender, EventArgs e)
        {
            var toolStripMenuItem = (ToolStripMenuItem)sender;
            var list = new List<ScrapBase>();
            while (dustbox.Count > 0)
            {
                var scrapBase = dustbox.Dequeue();
                if (!scrapBase.Equals(toolStripMenuItem.Tag))
                {
                    list.Add(scrapBase);
                }
                else
                {
                    scrapBook.AddScrapThenDo(scrapBase);
                }
            }
            dustbox.Clear();
            foreach (var item in list)
            {
                dustbox.Enqueue(item);
            }
            new ScrapEventArgs();
        }

        // Token: 0x06000201 RID: 513 RVA: 0x0000B0B8 File Offset: 0x000092B8
        private void miCapture_Click(object sender, EventArgs e)
        {
            StartCapture();
        }

        // Token: 0x06000202 RID: 514 RVA: 0x0000B0C0 File Offset: 0x000092C0
        private void miOption_Click(object sender, EventArgs e)
        {
            Option();
        }

        // Token: 0x06000203 RID: 515 RVA: 0x0000B0C8 File Offset: 0x000092C8
        private void miSetunaClose_Click(object sender, EventArgs e)
        {
            CloseSetuna();
        }

        // Token: 0x06000204 RID: 516 RVA: 0x0000B0D0 File Offset: 0x000092D0
        public void OnActiveScrapInList(object sender, EventArgs e)
        {
            var toolStripMenuItem = (ToolStripMenuItem)sender;
            if (toolStripMenuItem.Tag != null)
            {
                var scrapBase = (ScrapBase)toolStripMenuItem.Tag;
                if (scrapBase.Visible)
                {
                    scrapBase.Activate();
                }
                else if (scrapBase.StyleForm is Main.StyleItems.CompactScrap compactScrap)
                {
                    compactScrap.Close();
                }
            }
        }

        // Token: 0x06000205 RID: 517 RVA: 0x0000B0FE File Offset: 0x000092FE
        public void ScrapAdded(object sender, ScrapEventArgs e)
        {
        }

        // Token: 0x06000206 RID: 518 RVA: 0x0000B100 File Offset: 0x00009300
        public void ScrapRemoved(object sender, ScrapEventArgs e)
        {
        }

        // Token: 0x06000207 RID: 519 RVA: 0x0000B102 File Offset: 0x00009302
        private void button1_Click(object sender, EventArgs e)
        {
            StartCapture();
        }

        // Token: 0x06000208 RID: 520 RVA: 0x0000B10A File Offset: 0x0000930A
        private void button4_Click(object sender, EventArgs e)
        {
            Option();
        }

        // Token: 0x06000209 RID: 521 RVA: 0x0000B114 File Offset: 0x00009314
        public void ScrapCreated(object sender, ScrapEventArgs e)
        {
            var cstyle = optSetuna.FindStyle(optSetuna.Scrap.CreateStyleID);
            if (cstyle != null)
            {
                cstyle.Apply(ref e.scrap);
                return;
            }
        }

        // Token: 0x0600020A RID: 522 RVA: 0x0000B168 File Offset: 0x00009368
        public void ScrapInactived(object sender, ScrapEventArgs e)
        {
            if (optSetuna.Scrap.InactiveAlphaChange)
            {
                e.scrap.InactiveOpacity = 1.0 - optSetuna.Scrap.InactiveAlphaValue / 100.0;
            }
            else
            {
                e.scrap.InactiveOpacity = e.scrap.ActiveOpacity;
            }
            var inactiveLineChange = optSetuna.Scrap.InactiveLineChange;
        }

        // Token: 0x0600020B RID: 523 RVA: 0x0000B1E0 File Offset: 0x000093E0
        public void ScrapActivated(object sender, ScrapEventArgs e)
        {
            var inactiveAlphaChange = optSetuna.Scrap.InactiveAlphaChange;
            var inactiveLineChange = optSetuna.Scrap.InactiveLineChange;
        }

        // Token: 0x0600020C RID: 524 RVA: 0x0000B204 File Offset: 0x00009404
        public void ScrapInactiveMouseOver(object sender, ScrapEventArgs e)
        {
            if (optSetuna.Scrap.MouseOverAlphaChange)
            {
                e.scrap.RollOverOpacity = 1.0 - optSetuna.Scrap.MouseOverAlphaValue / 100.0;
            }
            else
            {
                e.scrap.RollOverOpacity = e.scrap.ActiveOpacity;
            }
            var mouseOverLineChange = optSetuna.Scrap.MouseOverLineChange;
        }

        // Token: 0x0600020D RID: 525 RVA: 0x0000B27C File Offset: 0x0000947C
        public void ScrapInactiveMouseOut(object sender, ScrapEventArgs e)
        {
            if (optSetuna.Scrap.InactiveAlphaChange)
            {
                e.scrap.InactiveOpacity = 1.0 - optSetuna.Scrap.InactiveAlphaValue / 100.0;
            }
            else
            {
                e.scrap.InactiveOpacity = e.scrap.ActiveOpacity;
            }
            var inactiveLineChange = optSetuna.Scrap.InactiveLineChange;
        }

        // Token: 0x0600020E RID: 526 RVA: 0x0000B2F4 File Offset: 0x000094F4
        private void button6_Click(object sender, EventArgs e)
        {
            optSetuna = SetunaOption.GetDefaultOption();
            var num = 0;
            num++;
        }

        // Token: 0x0600020F RID: 527 RVA: 0x0000B312 File Offset: 0x00009512
        private void button7_Click(object sender, EventArgs e)
        {
        }

        // Token: 0x06000210 RID: 528 RVA: 0x0000B314 File Offset: 0x00009514
        private void Mainform_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveMainWindowSize();
            SaveOption();
            foreach (HotKeyID item in Enum.GetValues(typeof(HotKeyID)))
            {
                optSetuna.UnregistHotKey(Handle, item);
            }
        }

        protected override void DisposeOwnedResources()
        {
            base.DisposeOwnedResources();

            // scrapBook 以前靠终结器关闭它持有的所有截图窗体，那发生在终结器线程上。
            // 在这里释放，关闭发生在拥有这些窗体的 UI 线程。
            if (scrapBook != null)
            {
                scrapBook.Dispose();
            }
        }

        // Token: 0x06000211 RID: 529 RVA: 0x0000B324 File Offset: 0x00009524
        private void Mainform_Load(object sender, EventArgs e)
        {
            base.Visible = false;
            LoadOption();
            // 边界的 DPI 来源统一走 WindowsAPI.GetWindowDpi，而不是 Control.DeviceDpi：
            // 两者在 net8 上相等，但显示器快照也由前者拼出，混用会让同一次换算里出现
            // 两个来源（见 WindowsAPI.GetWindowDpi）。读一次交给两步，边界与尺寸因此
            // 一定属于同一档。
            var dpi = WindowsAPI.GetWindowDpi(Handle);
            ApplyWindowSizeBounds(dpi);
            RestoreMainWindowSize(dpi);
            OptionApply();
            SaveOption();
            if (optSetuna.Setuna.ShowSplashWindow)
            {
                frmSplash = new SplashForm();
                base.AddOwnedForm(frmSplash);
                frmSplash.Show(this);
                frmSplash.SplashTimer.Start();
            }
            timPool.Start();
            Mainform.cap_form = new CaptureForm(optSetuna.Setuna);
            IsStart = true;

            SETUNA.Main.Layer.LayerManager.Instance.Init();
            SETUNA.Main.Cache.CacheManager.Instance.Init();
            delayInitTimer.Start();
        }

        // Token: 0x06000212 RID: 530 RVA: 0x0000B3B6 File Offset: 0x000095B6
        private void setunaIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                base.Activate();
                SETUNA.Main.Layer.LayerManager.Instance.RefreshLayer();
            }
        }

        private void setunaIcon_MouseDoubleClick(object sender, EventArgs e)
        {
            Option();
        }

        // Token: 0x06000213 RID: 531 RVA: 0x0000B3BE File Offset: 0x000095BE
        public void ScrapMenuOpening(object sender, ScrapMenuArgs e)
        {
            subMenu.Scrap = e.scrap;
            subMenu.Show(e.scrap, e.scrap.PointToClient(Cursor.Position));

            subMenu.MouseWheel -= SubMenu_MouseWheel;
            subMenu.MouseWheel += SubMenu_MouseWheel;
        }

        private void SubMenu_MouseWheel(object sender, MouseEventArgs e)
        {
            if (MouseWheelCallbackEvent != null)
            {
                MouseWheelCallbackEvent(sender, e);
            }
        }

        // Token: 0x06000214 RID: 532 RVA: 0x0000B3F2 File Offset: 0x000095F2
        private void button2_Click(object sender, EventArgs e)
        {
            GC.Collect();
        }

        // Token: 0x06000215 RID: 533 RVA: 0x0000B3FC File Offset: 0x000095FC
        private void timPool_Tick(object sender, EventArgs e)
        {
            if (_imgpool.Count == 0 || IsCapture || IsOption || !IsStart)
            {
                timPool.Stop();
                return;
            }

            for (var i = 0; i < _imgpool.Count; i++)
            {
                var scrap = _imgpool[i];
                if (scrap.IsDone)
                {
                    _imgpool.RemoveAt(i);

                    using (var scrapSource = scrap)
                    {
                        CreateScrapFromsource(scrapSource);
                    }

                    break;
                }
            }
        }

        // Token: 0x06000216 RID: 534 RVA: 0x0000B46C File Offset: 0x0000966C
        public void AddImageList(ScrapSource src)
        {
            _imgpool.Add(src);
            timPool.Start();
        }

        // Token: 0x06000217 RID: 535 RVA: 0x0000B488 File Offset: 0x00009688
        public void CreateScrapFromImage(Image image, Point location)
        {
            if (image == null)
            {
                return;
            }
            using (var bitmap = (Bitmap)image.Clone())
            {
                if (location == Point.Empty)
                {
                    location = Cursor.Position;
                }
                var x = location.X;
                var y = location.Y;
                scrapBook.AddScrap((Bitmap)bitmap.Clone(), x, y, bitmap.Width, bitmap.Height);
            }
        }

        // Token: 0x06000218 RID: 536 RVA: 0x0000B50C File Offset: 0x0000970C
        private void CreateScrapFromsource(ScrapSource src)
        {
            CreateScrapFromImage(src.GetImage(), src.GetPosition());
        }

        // Token: 0x06000219 RID: 537 RVA: 0x0000B520 File Offset: 0x00009720
        void ISingletonForm.DetectExternalStartup(string version, string[] args)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            // The pipe server already posts through the captured UI context. Keep
            // this guard for direct/test callers and for a context-less startup
            // before the main handle exists.
            if (InvokeRequired)
            {
                BeginInvoke(new Mainform.ExternalStartupDelegate(ExternalStartup), new object[]
                {
                    version,
                    args ?? Array.Empty<string>()
                });
                return;
            }

            ExternalStartup(version, args ?? Array.Empty<string>());
        }

        // Token: 0x0600021A RID: 538 RVA: 0x0000B550 File Offset: 0x00009750
        private void ExternalStartup(string version, string[] args)
        {
            if (Application.ProductVersion != version)
            {
                MessageBox.Show(Lang.T("Message.OtherVersionRunning"), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }
            if (args.Length > 0)
            {
                CommandRun(args);
                return;
            }
            if (optSetuna.Setuna.DupType == SetunaOption.SetunaOptionData.OpeningType.Capture)
            {
                StartCapture();
            }
        }

        // Token: 0x0600021B RID: 539 RVA: 0x0000B5A8 File Offset: 0x000097A8
        public void CommandRun(string[] args)
        {
            Console.WriteLine("-命令行参数--------------------");
            var num = 0;
            var rect = new Rectangle(0, 0, 0, 0);
            var fname = "";
            foreach (var text in args)
            {
                try
                {
                    var text2 = text;
                    var text3 = "";
                    if (text.Length > 3)
                    {
                        text3 = text2.Substring(0, 3);
                        if (text3.Substring(0, 1) == "/" && text3.Substring(2, 1) == ":")
                        {
                            text2 = text.Substring(text3.Length, text2.Length - text3.Length);
                        }
                        else
                        {
                            text3 = "";
                        }
                    }
                    if (text3.Length > 0)
                    {
                        if (text3 == "/R:")
                        {
                            var array = text2.Split(new char[]
                            {
                                ','
                            });
                            if (array.Length == 4)
                            {
                                rect = default(Rectangle);
                                rect.X = int.Parse(array[0]);
                                rect.Y = int.Parse(array[1]);
                                rect.Width = int.Parse(array[2]);
                                rect.Height = int.Parse(array[3]);
                                Console.WriteLine("[位置]" + rect.ToString());
                                goto IL_1C2;
                            }
                        }
                        if (text3 == "/P:")
                        {
                            fname = text2;
                        }
                        if (text3 == "/C:")
                        {
                            if (text2.ToUpper() == "OPTION")
                            {
                                num = 1;
                                goto IL_1C2;
                            }
                            if (text2.ToUpper() == "CAPTURE")
                            {
                                num = 2;
                                goto IL_1C2;
                            }
                            if (text2.ToUpper() == "SUBMENU")
                            {
                                num = 3;
                                goto IL_1C2;
                            }
                        }
                    }
                    AddImageList(new ScrapSourcePath(text2));
                    Console.WriteLine(text2);
                }
                catch
                {
                    Console.WriteLine("[Error]" + text);
                }
            IL_1C2:;
            }
            Console.WriteLine("---------------------------------------");
            if (rect.Width >= 10 && rect.Height >= 10)
            {
                CommandCutRect(rect, fname);
                return;
            }
            if (num != 0 && IsStart)
            {
                switch (num)
                {
                    case 1:
                        if (!IsOption)
                        {
                            Option();
                            return;
                        }
                        break;
                    case 2:
                        if (!IsCapture)
                        {
                            StartCapture();
                        }
                        break;
                    default:
                        return;
                }
            }
        }

        // Token: 0x0600021C RID: 540 RVA: 0x0000B80C File Offset: 0x00009A0C
        private void CommandCutRect(Rectangle rect, string fname)
        {
            using (var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format24bppRgb))
            {
                var point = new Point(rect.X, rect.Y);
                CaptureForm.CopyFromScreen(bitmap, point);
                if (fname == "")
                {
                    AddImageList(new ScrapSourceImage(bitmap, point));
                }
            }
        }

        // Token: 0x0600021D RID: 541 RVA: 0x0000B888 File Offset: 0x00009A88
        private void Mainform_Shown(object sender, EventArgs e)
        {
        }

        // Token: 0x0600021E RID: 542 RVA: 0x0000B88A File Offset: 0x00009A8A
        private void setunaIconMenu_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = false;
        }


        private void window_Tick(object sender, EventArgs e)
        {
            SETUNA.Main.WindowManager.Instance.Update();
        }

        private void SetAllScrapsActive(bool active)
        {
            if (allScrapActive == active)
            {
                return;
            }

            allScrapActive = active;

            if (active)
            {
                forms.ForEach(x => x.Visible = active);
                forms.Clear();

                Main.Layer.LayerManager.Instance.ResumeRefresh();
            }
            else
            {
                foreach (var item in scrapBook)
                {
                    if (item.Visible)
                    {
                        forms.Add(item);
                    }
                    else if ((item.StyleForm?.Visible ?? false) == true)
                    {
                        forms.Add(item.StyleForm);
                    }
                }

                forms.ForEach(x => x.Visible = active);

                Main.Layer.LayerManager.Instance.SuspendRefresh();
            }
        }

        private void delayInitTimer_Tick(object sender, EventArgs e)
        {
            if (SETUNA.Main.Cache.CacheManager.Instance.IsInit)
            {
                delayInitTimer.Enabled = false;
                SETUNA.Main.Layer.LayerManager.Instance.RefreshLayer();
                SETUNA.Main.Layer.LayerManager.Instance.DelayInit();
            }
        }



        // Token: 0x040000E1 RID: 225
        private SplashForm frmSplash;

        // Token: 0x040000E2 RID: 226
        public ScrapBook scrapBook;

        // Token: 0x040000E3 RID: 227
        public SetunaOption optSetuna;

        // Token: 0x040000E4 RID: 228
        public KeyItemBook keyBook;

        // Token: 0x040000E5 RID: 229
        public Queue<ScrapBase> dustbox;

        // Token: 0x040000E6 RID: 230
        private ClickCapture frmClickCapture;

        // Token: 0x040000E7 RID: 231
        private static CaptureForm cap_form;

        // Token: 0x040000E8 RID: 232
        private List<ScrapSource> _imgpool;

        // Token: 0x040000E9 RID: 233
        private bool _iscapture;

        // Token: 0x040000EA RID: 234
        private bool _isoption;

        // Token: 0x040000EB RID: 235
        private bool _isstart;

        private bool _hotKeysInitialized;

        private bool _isApplyingOptions;

        // Token: 0x02000041 RID: 65
        // (Invoke) Token: 0x0600026A RID: 618
        private delegate void ExternalStartupDelegate(string version, string[] args);

        public delegate void MouseWheelCallback(object sender, MouseEventArgs e);
        public event MouseWheelCallback MouseWheelCallbackEvent;

        private List<Form> forms = new List<Form>();
        private bool allScrapActive = true;
    }
}
