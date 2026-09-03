using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using SETUNA.Main.Option;
using SETUNA.Plugins;

namespace SETUNA.Main.Runtime
{
    /// <summary>
    /// 「把发布出来的单文件 exe 单独拷进一个空目录，它还能干活吗」——这个问题只有那个 exe
    /// 自己能回答。
    /// <para>
    /// 测试套件跑的是 <c>bin\</c> 里的 <c>SETUNA.dll</c>：那不是一个 bundle，不解包任何东西，
    /// 也不会暴露单文件特有的失败——嵌入资源取不到、托管依赖没被打包、
    /// <see cref="XmlSerializer"/> 的运行时代码生成不可用。所以自检必须是产品自己的一个入口，
    /// 而且必须自带样本：目标场景里旁边一个文件都没有。
    /// </para>
    /// <para>
    /// 不传 <c>--self-test</c> 时这个类一行都不执行。样本的构造是各格式容器头的第二份独立
    /// 表述，与解码器互不引用，因此两边同时错才会通过。
    /// </para>
    /// </summary>
    internal static class SelfTest
    {
        /// <summary>命令行开关。<c>Program.Main</c> 在竞选单实例之前处理它，所以自检永远不会去找正在运行的 SETUNA。</summary>
        public const string Switch = "--self-test";

        /// <summary>
        /// 跑完所有检查，把逐项结果写进 <paramref name="reportPath"/>（为空时写到 %TEMP%）。
        /// 返回进程退出码：0 全通过，1 有失败项。
        /// <para>
        /// 结果只进文件，不进控制台：<c>WinExe</c> 没有附着的控制台，从终端启动时
        /// <c>Console.WriteLine</c> 哪儿都不去。
        /// </para>
        /// </summary>
        public static int Run(string reportPath)
        {
            var report = new List<string>();
            var failures = 0;
            var sampleRoot = Path.Combine(
                Path.GetTempPath(), "SETUNA-selftest-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(sampleRoot);
            try
            {
                foreach (var check in Checks(sampleRoot))
                {
                    string detail;
                    bool passed;
                    try
                    {
                        detail = check.Value();
                        passed = detail == null;
                    }
                    catch (Exception ex)
                    {
                        detail = ex.GetType().Name + ": " + ex.Message;
                        passed = false;
                    }

                    if (!passed)
                    {
                        failures++;
                    }

                    report.Add((passed ? "PASS " : "FAIL ") + check.Key + (detail == null ? string.Empty : " — " + detail));
                }
            }
            finally
            {
                TryDeleteDirectory(sampleRoot);
            }

            report.Add(failures == 0
                ? "PASS self-test (" + Architecture + ")"
                : "FAIL self-test (" + Architecture + "): " + failures + " check(s) failed");

            Write(reportPath, report);

            return failures == 0 ? 0 : 1;
        }

        static string Architecture => IntPtr.Size == 8 ? "x64" : "x86";

        /// <summary>
        /// 每项检查返回 <see langword="null"/> 表示通过，否则返回失败说明。顺序有意义：
        /// 先确认这确实是个单文件产物，否则后面每一项都是在测一个普通目录构建。
        /// </summary>
        static IEnumerable<KeyValuePair<string, Func<string>>> Checks(string sampleRoot)
        {
            yield return Check("single-file-bundle", TheProcessIsASingleFileBundle);
            yield return Check("no-companion-files", NothingSitsBesideTheExecutable);
            yield return Check("png", () => Decodes(sampleRoot, "sample.png", FormatSamples.Png(), 4, 3));
            yield return Check("svg", () => Decodes(sampleRoot, "sample.svg", FormatSamples.Svg(), 8, 6));
            yield return Check("psd", () => Decodes(sampleRoot, "sample.psd", FormatSamples.Psd(), 2, 1));
            yield return Check("ico", () => Decodes(sampleRoot, "sample.ico", FormatSamples.Ico(), 2, 2));
            yield return Check("tga", () => Decodes(sampleRoot, "sample.tga", FormatSamples.Tga(), 2, 2));
            yield return Check("webp-decode", () => Decodes(sampleRoot, "sample.webp", FormatSamples.WebP(), 3, 2));
            yield return Check("webp-native-library", TheWebPLibraryIsExtractedAndLoaded);
            yield return Check("webp-encode", WebPEncodingRoundTrips);
            yield return Check("option-xml", TheOptionFileRoundTrips);
        }

        static KeyValuePair<string, Func<string>> Check(string name, Func<string> body)
        {
            return new KeyValuePair<string, Func<string>>(name, body);
        }

        /// <summary>
        /// 单文件 bundle 里 <see cref="Assembly.Location"/> 是空串——程序集从 bundle 里加载，
        /// 磁盘上没有对应文件。这一项失败说明自检跑在普通目录构建上，下面的结论不适用于发布产物。
        /// </summary>
        static string TheProcessIsASingleFileBundle()
        {
            var location = Assembly.GetExecutingAssembly().Location;

            return string.IsNullOrEmpty(location)
                ? null
                : "the assembly was loaded from " + location + ", so this is not a single-file bundle";
        }

        /// <summary>
        /// 「拷到别的目录里也能跑」这条保证在运行时的直接形式：exe 旁边除了它自己和可选的 pdb
        /// 不该有别的东西。
        /// </summary>
        static string NothingSitsBesideTheExecutable()
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return "the process path is unavailable, so companion files cannot be checked";
            }

            var directory = Path.GetDirectoryName(executable);
            var expected = Path.GetFileName(executable);
            var symbols = Path.ChangeExtension(expected, ".pdb");
            var unexpected = new List<string>();

            foreach (var entry in Directory.GetFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                if (!string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, symbols, StringComparison.OrdinalIgnoreCase))
                {
                    unexpected.Add(name);
                }
            }

            return unexpected.Count == 0
                ? null
                : "unexpected companion(s) next to the executable: " + string.Join(", ", unexpected);
        }

        /// <summary>
        /// 走的是生产入口 <see cref="BitmapUtils.FromPath"/>，包括它的格式嗅探，
        /// 而不是直接调某个解码器——发布产物里可能失效的正是这条整链。
        /// </summary>
        static string Decodes(string sampleRoot, string fileName, byte[] sample, int width, int height)
        {
            var path = Path.Combine(sampleRoot, fileName);
            File.WriteAllBytes(path, sample);

            using (var decoded = BitmapUtils.FromPath(path))
            {
                if (decoded == null)
                {
                    return "FromPath returned null for a " + sample.Length + "-byte sample";
                }

                return decoded.Width == width && decoded.Height == height
                    ? null
                    : "decoded " + decoded.Width + "x" + decoded.Height + " instead of " + width + "x" + height;
            }
        }

        /// <summary>
        /// 原生 libwebp 是唯一不由 SDK 打包的依赖：它作为嵌入资源解包到受控目录后
        /// <c>LoadLibrary</c>。这一项确认解包真的落地了，而不是靠机器上恰好装过一份。
        /// </summary>
        static string TheWebPLibraryIsExtractedAndLoaded()
        {
            if (!ResourceExtractor.ExtractWebP())
            {
                return "ExtractWebP reported failure";
            }

            var expected = Path.Combine(
                ResourceExtractor.NativeDirectory, IntPtr.Size == 8 ? "libwebp_x64.dll" : "libwebp_x86.dll");

            return File.Exists(expected) ? null : "the extracted library is missing: " + expected;
        }

        static string WebPEncodingRoundTrips()
        {
            using (var source = FormatSamples.Bitmap(3, 2))
            using (var webp = new WebPWrapper.WebP())
            using (var decoded = webp.Decode(webp.EncodeLossless(source)))
            {
                return decoded.Width == source.Width && decoded.Height == source.Height
                    ? null
                    : "a lossless round trip returned " + decoded.Width + "x" + decoded.Height;
            }
        }

        /// <summary>
        /// <see cref="XmlSerializer"/> 在运行时生成代码，是自包含发布里最容易出问题的一环，
        /// 而配置读不出来等于用户所有设置丢失。这里连 <c>GetAllType()</c> 里的样式类型一起过。
        /// </summary>
        static string TheOptionFileRoundTrips()
        {
            var option = SetunaOption.GetDefaultOption();
            option.MainWindowWidth = 321;
            option.MainWindowHeight = 234;
            option.MainWindowDpi = 168;

            var serializer = new XmlSerializer(typeof(SetunaOption), SetunaOption.GetAllType());
            using (var buffer = new MemoryStream())
            {
                serializer.Serialize(buffer, option);
                buffer.Position = 0;

                var restored = (SetunaOption)serializer.Deserialize(buffer);
                if (restored.MainWindowWidth != 321
                    || restored.MainWindowHeight != 234
                    || restored.MainWindowDpi != 168)
                {
                    return "the window size did not survive the round trip";
                }

                return restored.Styles.Count == option.Styles.Count
                    ? null
                    : "restored " + restored.Styles.Count + " styles instead of " + option.Styles.Count;
            }
        }

        static void Write(string reportPath, List<string> report)
        {
            var target = string.IsNullOrEmpty(reportPath)
                ? Path.Combine(Path.GetTempPath(), "SETUNA-selftest-" + Architecture + ".txt")
                : reportPath;

            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(target));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllLines(target, report);
            }
            catch (Exception ex)
            {
                // 报告写不出去不能改变退出码：调用方靠退出码判断成败，报告只是细节。
                Console.WriteLine("SelfTest: could not write " + target + ": " + ex.Message);
            }
        }

        static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
                // 样本目录在 %TEMP% 里，删不掉也只是留下几 KB。
            }
        }
    }
}
