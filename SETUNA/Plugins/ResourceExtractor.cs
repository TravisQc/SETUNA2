using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SETUNA.Plugins
{
    public static class ResourceExtractor
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibrary(string lpFileName);

        static bool isWebPReady;

        /// <summary>
        /// 内嵌原生 DLL 的提取目录。放在应用自有的本地应用数据目录下：
        /// 总是可写（程序目录可能是 Program Files），且不随进程当前工作目录变化。
        /// </summary>
        public static string NativeDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SETUNA",
            "native",
            IntPtr.Size == 8 ? "x64" : "x86");

        /// <summary>
        /// 提取并加载 libwebp 原生 DLL。返回是否就绪。
        /// 失败时不置位就绪标记，后续调用会重新尝试。
        /// </summary>
        public static bool ExtractWebP()
        {
            if (isWebPReady)
            {
                return true;
            }

            var fileName = IntPtr.Size == 8 ? "libwebp_x64.dll" : "libwebp_x86.dll";
            var resourceName = $"{nameof(SETUNA)}.{nameof(Plugins)}.{fileName}";

            try
            {
                var targetPath = Path.Combine(NativeDirectory, fileName);
                if (!ExtractResourceToFile(resourceName, targetPath))
                {
                    return false;
                }

                // 按完整路径预加载：WebPWrapper 里的 47 个 DllImport 都用裸文件名，
                // 而 Windows 按模块名匹配已加载模块，因此后续 P/Invoke 会命中这一份，
                // 不必逐条改写声明，也不再依赖系统 DLL 搜索顺序。
                if (LoadLibrary(targetPath) == IntPtr.Zero)
                {
                    Console.WriteLine($"ResourceExtractor: LoadLibrary failed for {targetPath}, error {Marshal.GetLastWin32Error()}");
                    return false;
                }

                isWebPReady = true;
                return true;
            }
            catch (Exception ex)
            {
                // 失败可被观测，而不是静默地把后续解码留给一个不存在的 DLL。
                Console.WriteLine("ResourceExtractor: failed to provision libwebp: " + ex);
                return false;
            }
        }

        /// <summary>
        /// 把内嵌资源写到 <paramref name="targetPath"/>。已存在且长度一致时跳过。
        /// 返回目标文件是否就绪。
        /// </summary>
        public static bool ExtractResourceToFile(string resourceName, string targetPath)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (var resource = assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null)
                {
                    // 资源名拼错或资源未被嵌入：按失败处理，而不是抛空引用。
                    Console.WriteLine("ResourceExtractor: embedded resource not found: " + resourceName);
                    return false;
                }

                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (IsUpToDate(targetPath, resource.Length))
                {
                    return true;
                }

                // 先写临时文件再原子替换：直接写目标时若中途崩溃，
                // 留下的截断 DLL 会在下次启动被当成有效文件跳过。
                var temporaryPath = targetPath + ".tmp";
                try
                {
                    using (var target = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write))
                    {
                        // CopyTo 会读到流结束；原来的单次 s.Read 不保证返回请求的全部字节。
                        resource.CopyTo(target);
                    }

                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    File.Move(temporaryPath, targetPath);
                    return true;
                }
                catch
                {
                    TryDelete(temporaryPath);
                    throw;
                }
            }
        }

        static bool IsUpToDate(string targetPath, long expectedLength)
        {
            if (!File.Exists(targetPath))
            {
                return false;
            }

            return new FileInfo(targetPath).Length == expectedLength;
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
