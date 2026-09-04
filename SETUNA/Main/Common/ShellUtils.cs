using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SETUNA.Main.Localization;

namespace SETUNA.Main.Common
{
    /// <summary>
    /// 把网址和文件夹交给系统外壳打开。
    /// <para>
    /// 这个类存在的全部理由是 <see cref="ProcessStartInfo.UseShellExecute"/> 的默认值在
    /// 两个运行时上是相反的：.NET Framework 4.8 默认 <c>true</c>，所以
    /// <c>Process.Start("https://…")</c> 走 ShellExecute、由注册表决定用浏览器打开；
    /// .NET Core 3.0 起默认 <c>false</c>，同一行代码变成「把这串字符当可执行文件启动」，
    /// 于是一律抛 <c>Win32Exception: An error occurred trying to start process</c>——
    /// 目录是 <c>(5) 拒绝访问</c>，网址是 <c>(2) 系统找不到指定的文件</c>（实测）。
    /// 迁移到 .NET 8 之后，「打开缓存目录」和「关于」页的两个链接就是这么坏掉的。
    /// </para>
    /// <para>
    /// 因此所有打开外部目标的代码 MUST 走这里，MUST NOT 各自调
    /// <see cref="Process.Start(string)"/>——那个重载没有地方声明
    /// <c>UseShellExecute</c>，写出来就是错的。失败一律提示后返回 <c>false</c>，
    /// MUST NOT 让异常冒到未处理异常对话框：这两个入口都是「顺手点一下」的操作，
    /// 打不开不是需要终止程序的理由。
    /// </para>
    /// </summary>
    internal static class ShellUtils
    {
        /// <summary>
        /// 用默认浏览器打开 <paramref name="url"/>，成功返回 <c>true</c>。
        /// </summary>
        public static bool OpenUrl(string url)
        {
            if (!IsHttpUrl(url))
            {
                Trace.TraceError("ShellUtils.OpenUrl refused a non-http(s) target: " + url);
                return ReportFailure("Message.OpenUrlFailed", url);
            }

            return ShellExecute(url, "Message.OpenUrlFailed");
        }

        /// <summary>
        /// 用文件管理器打开 <paramref name="path"/>，成功返回 <c>true</c>。
        /// </summary>
        public static bool OpenFolder(string path)
        {
            if (!EnsureFolder(path))
            {
                return ReportFailure("Message.OpenFolderFailed", path);
            }

            return ShellExecute(path, "Message.OpenFolderFailed");
        }

        /// <summary>
        /// <paramref name="url"/> 是否是能交给浏览器的 http/https 绝对地址。
        /// <para>
        /// 只放行这两种协议。ShellExecute 把目标交给注册表解析，
        /// <c>C:\…\x.exe</c>、<c>shell:startup</c>、任何注册过的自定义协议都会被照着启动；
        /// 调用点手里的链接来自控件的 <see cref="LinkLabel.Link.LinkData"/> 或
        /// <see cref="Control.Text"/>，不该带着「能启动本地程序」这份权限。
        /// </para>
        /// </summary>
        public static bool IsHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 确保 <paramref name="path"/> 是一个存在的目录，不存在就创建；成功返回 <c>true</c>。
        /// <para>
        /// 缓存目录本来是 <see cref="Cache.CacheManager"/> 初始化时建好的，但用户手动删掉之后
        /// 「打开缓存目录」不该只是弹个错误——ShellExecute 对不存在的路径只会失败，而按钮的
        /// 意思是「让我看看这个位置」，把空目录建出来再打开正是用户要的结果。
        /// </para>
        /// </summary>
        public static bool EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    return true;
                }

                // 路径指向的是文件而不是目录时，CreateDirectory 抛 IOException，落到下面的
                // catch 里报「打不开」——正是应有的结论，不需要单独判断。
                Directory.CreateDirectory(path);

                return Directory.Exists(path);
            }
            catch (Exception ex)
            {
                Trace.TraceError("ShellUtils could not prepare the folder " + path + ": " + ex);
                return false;
            }
        }

        static bool ShellExecute(string target, string failureKey)
        {
            try
            {
                // 返回 null 表示外壳复用了已有进程（浏览器本来就开着时就是这样），不是失败。
                using (Process.Start(StartInfoFor(target)))
                {
                }

                return true;
            }
            catch (Exception ex)
            {
                // 记细节给开发者，给用户的是能看懂的说明而不是堆栈跟踪。默认浏览器注册项
                // 损坏、目录刚好被删、组策略拦下 ShellExecute 都会走到这里。
                Trace.TraceError("ShellUtils could not open " + target + ": " + ex);
                return ReportFailure(failureKey, target);
            }
        }

        /// <summary>
        /// 交给 <see cref="Process.Start(ProcessStartInfo)"/> 的启动信息。
        /// <c>UseShellExecute = true</c> 这一行就是修复本身，理由见类型注释。
        /// </summary>
        internal static ProcessStartInfo StartInfoFor(string target)
        {
            return new ProcessStartInfo(target)
            {
                UseShellExecute = true,
            };
        }

        static bool ReportFailure(string messageKey, string target)
        {
            // 显式指定 owner：选项窗体显示期间是 TopMost 的，无主的对话框会被它压在
            // 后面，看上去就是程序卡住了。ActiveForm 为 null 时这个重载退化成无主，
            // 和其他失败提示的行为一致。
            MessageBox.Show(
                Form.ActiveForm,
                Lang.T(messageKey, target ?? string.Empty),
                Application.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Hand);

            return false;
        }
    }
}
