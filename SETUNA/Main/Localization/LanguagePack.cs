using System;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace SETUNA.Main.Localization
{
    /// <summary>
    /// 一种语言的资源集。
    /// <para>
    /// 按资源基名显式构造 <see cref="ResourceManager"/>，不走
    /// <see cref="CultureInfo.CurrentUICulture"/> 的自动区域回退：资源文件名用的是
    /// <c>Strings_en</c> 这种下划线形式（避免 MSBuild 生成卫星程序集），它不是合法
    /// 区域名，自动回退链根本认不出来。
    /// </para>
    /// <para>
    /// 副作用是切换界面语言不需要改动线程的
    /// <see cref="CultureInfo.CurrentUICulture"/>，因此数字与日期的格式化行为完全
    /// 不受影响。
    /// </para>
    /// </summary>
    internal sealed class LanguagePack
    {
        private const string BaseNamePrefix = "SETUNA.Resources.Lang.";

        private readonly ResourceManager _resources;

        private LanguagePack(AppLanguage language, string baseName)
        {
            Language = language;
            BaseName = baseName;
            _resources = new ResourceManager(baseName, typeof(LanguagePack).Assembly);
        }

        public AppLanguage Language { get; }

        public string BaseName { get; }

        /// <summary>中性资源集（简体中文）。</summary>
        public static LanguagePack CreateNeutral()
        {
            return new LanguagePack(AppLanguage.ChineseSimplified, BaseNamePrefix + "Strings");
        }

        /// <summary>
        /// 指定语言的资源集。<see cref="AppLanguage.Auto"/> MUST 在调用前先经
        /// <see cref="AppLanguages.Resolve"/> 解析成具体语言。
        /// </summary>
        public static LanguagePack Create(AppLanguage language)
        {
            switch (language)
            {
                case AppLanguage.English:
                    return new LanguagePack(AppLanguage.English, BaseNamePrefix + "Strings_en");
                case AppLanguage.ChineseSimplified:
                    return CreateNeutral();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(language),
                        language,
                        "语言必须先解析成具体语言，Auto 没有对应的资源集。");
            }
        }

        /// <summary>
        /// 取一条文字。键不存在时返回 <c>null</c>，由调用方决定回退——这里 MUST NOT 把
        /// 键名当文字返回，否则资源键会漏到界面上。
        /// </summary>
        public string Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                // InvariantCulture：资源已经按基名选定，这里传哪个区域都不影响查找结果，
                // 显式传不变区域是为了不受线程区域设置影响。
                return _resources.GetString(key, CultureInfo.InvariantCulture);
            }
            catch (MissingManifestResourceException)
            {
                // 资源集本身没被嵌进程序集。属于构建配置问题，不应让界面崩掉。
                return null;
            }
        }
    }
}
