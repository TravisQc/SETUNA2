using System;
using System.Windows.Forms;
using Microsoft.Win32;
using SETUNA.Main.Localization;

namespace SETUNA.Main.Startup
{
    internal static class AutoStartup
    {
        private static string Key = "SETUNA_AutoStartup";

        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Set(bool enabled)
        {
            try
            {
                using (var registryKey = GetRegistryKey())
                {
                    if (registryKey == null)
                    {
                        // 打开 Run 键失败（权限或策略）。按失败处理，
                        // 不再靠 catch 兜住一个空引用异常。
                        ShowFailure(Lang.T("Message.AutoStartupRegistryUnavailable"));
                        return false;
                    }

                    if (enabled)
                    {
                        registryKey.SetValue(Key, QuoteExecutablePath(Application.ExecutablePath));
                    }
                    else
                    {
                        if (registryKey.GetValue(Key, null) != null)
                        {
                            registryKey.DeleteValue(Key);
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                // 记录细节给开发者，呈现给用户的是可理解的说明而不是堆栈跟踪。
                Console.WriteLine("AutoStartup.Set failed: " + ex);
                ShowFailure(Lang.T(enabled ? "Message.AutoStartupEnableFailed" : "Message.AutoStartupDisableFailed"));
                return false;
            }
        }

        public static bool IsSetup()
        {
            try
            {
                using (var registryKey = GetRegistryKey())
                {
                    if (registryKey == null)
                    {
                        return false;
                    }

                    foreach (var item in registryKey.GetValueNames())
                    {
                        if (item.Equals(Key, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AutoStartup.IsSetup failed: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Run 键的值由 Windows 按命令行解析：路径含空格时必须加引号，
        /// 否则 <c>C:\Program Files\SETUNA\SETUNA.exe</c> 会被当成
        /// <c>C:\Program</c> 加参数 <c>Files\SETUNA\SETUNA.exe</c>。
        /// </summary>
        public static string QuoteExecutablePath(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                return executablePath;
            }

            if (executablePath.StartsWith("\"", StringComparison.Ordinal)
                && executablePath.EndsWith("\"", StringComparison.Ordinal))
            {
                return executablePath;
            }

            return "\"" + executablePath + "\"";
        }

        public static RegistryKey GetRegistryKey()
        {
            return Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        }

        static void ShowFailure(string message)
        {
            MessageBox.Show(message, "SETUNA2", MessageBoxButtons.OK, MessageBoxIcon.Hand);
        }
    }
}
