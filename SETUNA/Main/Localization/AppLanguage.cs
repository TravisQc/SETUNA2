using System;
using System.Globalization;

namespace SETUNA.Main.Localization
{
    /// <summary>
    /// 受支持的界面语言。
    /// <para>
    /// <see cref="Auto"/> 表示「跟随系统区域设置」，是一个会被保存下来的用户选择，
    /// 而不是推断结果：用户选了「跟随系统」之后换一台不同区域的机器，界面应当跟着
    /// 变，所以配置里存的必须是 <see cref="Auto"/> 本身。
    /// </para>
    /// </summary>
    internal enum AppLanguage
    {
        Auto = 0,
        ChineseSimplified,
        English,
    }

    /// <summary>
    /// <see cref="AppLanguage"/> 与配置文件中的字符串之间的映射，以及「跟随系统」的
    /// 推断规则。
    /// <para>
    /// 配置里存字符串而不是枚举：<see cref="System.Xml.Serialization.XmlSerializer"/>
    /// 遇到无法识别的枚举值会抛异常，那会让「新版本写了未来语言的配置被旧版本读取」
    /// 变成启动失败。字符串则退化成一个未知值，可以按 <see cref="AppLanguage.Auto"/>
    /// 安全处理。
    /// </para>
    /// </summary>
    internal static class AppLanguages
    {
        /// <summary>「跟随系统」在配置文件中的表示。缺失元素反序列化为 <c>null</c>，与空串同义。</summary>
        public const string AutoValue = "";

        public const string ChineseSimplifiedValue = "zh-CN";
        public const string EnglishValue = "en";

        /// <summary>
        /// 把配置里的字符串解析为语言。无法识别的值（含 <c>null</c>、空白、未来版本
        /// 写入的语言标识）一律解析为 <see cref="AppLanguage.Auto"/>——MUST NOT 抛异常，
        /// 否则一个陌生的配置值就能让应用起不来。
        /// </summary>
        public static AppLanguage Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return AppLanguage.Auto;
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return AppLanguage.Auto;
            }

            if (string.Equals(trimmed, ChineseSimplifiedValue, StringComparison.OrdinalIgnoreCase))
            {
                return AppLanguage.ChineseSimplified;
            }

            if (string.Equals(trimmed, EnglishValue, StringComparison.OrdinalIgnoreCase))
            {
                return AppLanguage.English;
            }

            return AppLanguage.Auto;
        }

        /// <summary>把语言写回配置文件的字符串形式。</summary>
        public static string ToConfigValue(AppLanguage language)
        {
            switch (language)
            {
                case AppLanguage.ChineseSimplified:
                    return ChineseSimplifiedValue;
                case AppLanguage.English:
                    return EnglishValue;
                default:
                    return AutoValue;
            }
        }

        /// <summary>
        /// 把语言解析成一种具体语言：<see cref="AppLanguage.Auto"/> 按
        /// <paramref name="uiCulture"/> 推断，其余原样返回。
        /// </summary>
        public static AppLanguage Resolve(AppLanguage language, CultureInfo uiCulture)
        {
            return language == AppLanguage.Auto ? InferFromCulture(uiCulture) : language;
        }

        /// <summary>
        /// 由系统界面区域设置推断语言。命中简体中文用简体中文，其余一律用英语。
        /// <para>
        /// 繁体中文（<c>zh-Hant</c>/<c>zh-TW</c>/<c>zh-HK</c>/<c>zh-MO</c>）归到英语一侧：
        /// 这次没有繁体资源，把简体文案给繁体用户是主动选择了一个错误的文案，而英语
        /// 至少是正确且可读的。
        /// </para>
        /// <para>
        /// 只依赖入参、不读任何环境状态，因此可以用构造出来的
        /// <see cref="CultureInfo"/> 直接做表驱动测试。
        /// </para>
        /// </summary>
        public static AppLanguage InferFromCulture(CultureInfo uiCulture)
        {
            if (uiCulture == null)
            {
                return AppLanguage.English;
            }

            // 沿 Parent 链向上找中文分支。zh-CN 的父级是 zh-Hans，zh-Hans 的父级是 zh，
            // 所以只要判断链上是否出现 zh-Hans 就能覆盖全部简体子区域；直接比较
            // zh-CN/zh-SG 会漏掉将来新增的简体区域。
            for (var culture = uiCulture; culture != null && !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
            {
                if (string.Equals(culture.Name, "zh-Hans", StringComparison.OrdinalIgnoreCase))
                {
                    return AppLanguage.ChineseSimplified;
                }

                if (string.Equals(culture.Name, "zh-Hant", StringComparison.OrdinalIgnoreCase))
                {
                    return AppLanguage.English;
                }

                // 中性 zh 没有 Hans/Hant 之分。历史上简体是它的事实含义，且本项目的
                // 既有文案就是简体，所以归到简体。
                if (string.Equals(culture.Name, "zh", StringComparison.OrdinalIgnoreCase))
                {
                    return AppLanguage.ChineseSimplified;
                }

                // Parent 链在到达不变区域（Name 为空）前必须终止，否则 Parent 恒等于
                // 自身的区域会让循环停不下来。
                if (ReferenceEquals(culture, culture.Parent))
                {
                    break;
                }
            }

            return AppLanguage.English;
        }
    }
}
