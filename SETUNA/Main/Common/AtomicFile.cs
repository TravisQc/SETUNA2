using System;
using System.IO;

namespace SETUNA.Main.Common
{
    /// <summary>
    /// 「先写临时文件、再原子替换」的文件写入。
    /// 直接以 <see cref="FileMode.Create"/> 打开目标会先截断再写，
    /// 写入中途失败就留下一个半截的文件——对配置文件来说等于用户设置全部丢失。
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// 把 <paramref name="writeContent"/> 产出的内容写入 <paramref name="targetFile"/>。
        /// 写入过程抛出异常时，目标文件保持原样、临时文件被清理，异常照原样向上传播。
        /// </summary>
        public static void Write(string targetFile, Action<Stream> writeContent)
        {
            if (targetFile == null)
            {
                throw new ArgumentNullException(nameof(targetFile));
            }

            if (writeContent == null)
            {
                throw new ArgumentNullException(nameof(writeContent));
            }

            // 临时文件与目标同目录，因此天然同卷，替换是原子的。
            var temporaryFile = targetFile + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write))
                {
                    writeContent(stream);
                }

                Replace(temporaryFile, targetFile);
            }
            catch
            {
                TryDelete(temporaryFile);
                throw;
            }
        }

        static void Replace(string temporaryFile, string targetFile)
        {
            if (File.Exists(targetFile))
            {
                // 不保留备份：目标的旧内容此刻已经完整地是「上一个成功写入的版本」。
                File.Replace(temporaryFile, targetFile, null);
            }
            else
            {
                File.Move(temporaryFile, targetFile);
            }
        }

        static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // 清理失败不能盖掉真正的写入失败。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
