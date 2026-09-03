using System;
using System.Net;
using System.Windows.Forms;

namespace SETUNA.Main.Runtime
{
    /// <summary>
    /// 进程启动时必须生效的运行时设置。
    /// <para>
    /// 这些设置以前写在 <c>app.config</c> 的 <c>&lt;runtime&gt;</c> 节里，随
    /// <c>SETUNA.exe.config</c> 一起分发。单文件分发不再有配置文件，而 CLR 读取
    /// <c>&lt;runtime&gt;</c> 的时机早于 <c>Main</c>，运行时无法补救，所以这两个
    /// 开关只能改由代码设置。
    /// </para>
    /// </summary>
    internal static class RuntimeConfiguration
    {
        /// <summary>
        /// 应用运行时设置。MUST 是 <c>Main</c> 的第一条语句：
        /// <see cref="AppContext"/> 开关一旦被 <see cref="ServicePointManager"/>
        /// 等网络类型的静态初始化读走就不再起作用。
        /// </summary>
        public static void Apply()
        {
            // 与被删除的 AppContextSwitchOverrides 逐字等价：关掉这两个「不启用」
            // 开关，即启用强加密与系统默认 TLS 版本。它们本是 .NET Framework 的兼容
            // 开关，net8 不再读取，显式设置只为让行为不依赖任何一起分发的文件。
            AppContext.SetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", false);
            AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);

            // SystemDefault（值 0）表示「由 Windows/SCHANNEL 策略决定」，不是固定
            // 协议列表，因此服务器策略变化时不需要改代码。写成显式赋值使「用系统
            // 策略」这件事可以被测试直接断言。
            ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
        }

        /// <summary>
        /// 校验进程真的报告了 Per-Monitor V2。符合预期时返回 <see langword="null"/>，
        /// 否则返回可诊断的说明。
        /// <para>
        /// 感知级别在进程创建时由链接进 exe 的清单确定，
        /// <c>ApplicationConfiguration.Initialize()</c> 里的 <c>SetHighDpiMode</c>
        /// 只是同一个值的兜底。任何一处失配都会让 <c>AutoScaleMode.Dpi</c> 退化成
        /// 单一 DPI 布局，而运行时不会为此报错——所以启动时读一次实际值。
        /// MUST 在 <c>Initialize()</c> 之后调用：更早读会让 WinForms 提前完成缩放
        /// 初始化，反而使 <c>SetHighDpiMode</c> 失效。
        /// </para>
        /// </summary>
        public static string DescribeHighDpiModeMismatch()
        {
            return DescribeHighDpiModeMismatch(Application.HighDpiMode);
        }

        /// <summary>
        /// 纯函数形式，便于对每一个 <see cref="HighDpiMode"/> 取值断言。测试宿主自身
        /// 没有清单，无法把进程真实感知级别当作被测输入。
        /// </summary>
        internal static string DescribeHighDpiModeMismatch(HighDpiMode actual)
        {
            if (actual == HighDpiMode.PerMonitorV2)
            {
                return null;
            }

            return "High DPI mode is " + actual + ", expected " + HighDpiMode.PerMonitorV2
                + ". Per-monitor layout is disabled: check app.manifest's dpiAwareness element"
                + " and the ApplicationHighDpiMode project property.";
        }
    }
}
