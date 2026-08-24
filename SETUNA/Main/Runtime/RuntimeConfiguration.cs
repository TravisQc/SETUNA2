using System;
using System.Net;

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
            // 开关，即启用强加密与系统默认 TLS 版本。目标框架 4.8 下这本就是默认
            // 值，显式设置是为了让行为不依赖任何一起分发的文件。
            AppContext.SetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", false);
            AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);

            // SystemDefault（值 0）表示「由 Windows/SCHANNEL 策略决定」，不是固定
            // 协议列表，因此服务器策略变化时不需要改代码。写成显式赋值使「用系统
            // 策略」这件事可以被测试直接断言。
            ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
        }
    }
}
