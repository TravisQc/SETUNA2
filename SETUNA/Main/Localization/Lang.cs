using System;
using System.Diagnostics;
using System.Globalization;

namespace SETUNA.Main.Localization
{
    /// <summary>
    /// 界面语言的单一入口：当前语言、按键取文字，以及语言变更通知。
    /// <para>
    /// 回退链是「当前语言 → 中性（简体中文）」。设计器拥有的控件文字**不进**中性资源集，
    /// 它们的简体中文来源就是 <c>*.Designer.cs</c> 里的字面量，因此那类键查不到时
    /// 正确行为是「不动控件」，由 <see cref="LocalizationApplier"/> 负责。
    /// </para>
    /// </summary>
    internal static class Lang
    {
        private static readonly object Sync = new object();
        private static readonly LanguagePack Neutral = LanguagePack.CreateNeutral();

        /// <summary>
        /// 除中性资源集之外的所有语言包。<see cref="IsKnownKey"/> 需要跨语言查一个键
        /// 是否登记过，新增语言时只要在这里加一项。
        /// </summary>
        private static readonly LanguagePack[] AllPacks =
        {
            LanguagePack.Create(AppLanguage.English),
        };

        private static LanguagePack _current;
        private static AppLanguage _selected = AppLanguage.Auto;

        static Lang()
        {
            // 在读到配置之前就按系统区域设置生效，而不是先停在中性资源集上。
            // 配置是在主窗口的 Load 里读的，那已经晚于窗体控件的创建；如果这里默认
            // 简体中文，英语系统的首次启动会先按简体应用一遍文字。默认值直接等于
            // 「跟随系统」的推断结果，这个顺序问题就不存在了。
            _current = LanguagePack.Create(AppLanguages.InferFromCulture(CultureInfo.CurrentUICulture));
        }

        /// <summary>
        /// 语言变更后触发。窗体在此重新应用文字。
        /// <para>
        /// 这是静态事件，会持有订阅者的引用：订阅方 MUST 在自己的确定性释放路径上退订，
        /// 否则每个开过的窗体都会被这个事件一直留住。<see cref="Common.BaseForm"/> 已经
        /// 成对处理，其他类型自行接入时必须照做。
        /// </para>
        /// </summary>
        public static event EventHandler LanguageChanged;

        /// <summary>用户的选择，可能是 <see cref="AppLanguage.Auto"/>。写回配置时用这个值。</summary>
        public static AppLanguage Selected
        {
            get
            {
                lock (Sync)
                {
                    return _selected;
                }
            }
        }

        /// <summary>当前实际生效的语言，永远是一种具体语言。</summary>
        public static AppLanguage Effective
        {
            get
            {
                lock (Sync)
                {
                    return _current.Language;
                }
            }
        }

        /// <summary>
        /// 设置语言。<paramref name="language"/> 为 <see cref="AppLanguage.Auto"/> 时按
        /// 系统界面区域设置推断实际语言，但记住的选择仍是 <see cref="AppLanguage.Auto"/>。
        /// 实际语言没有变化时不触发 <see cref="LanguageChanged"/>。
        /// </summary>
        public static void SetLanguage(AppLanguage language)
        {
            bool changed;
            lock (Sync)
            {
                var effective = AppLanguages.Resolve(language, CultureInfo.CurrentUICulture);
                changed = _selected != language || _current.Language != effective;
                _selected = language;
                if (_current.Language != effective)
                {
                    _current = LanguagePack.Create(effective);
                }
            }

            if (changed)
            {
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>从配置文件的字符串设置语言。</summary>
        public static void SetLanguage(string configValue)
        {
            SetLanguage(AppLanguages.Parse(configValue));
        }

        /// <summary>
        /// 取一条运行时文字。两个资源集都没有该键时以可诊断的方式失败：调试构建直接
        /// 断言中断，发布构建记录诊断并返回带标记的键名。MUST NOT 返回空字符串——空标签
        /// 看起来像渲染问题，会被当成偶发现象忽略过去。
        /// </summary>
        public static string T(string key)
        {
            LanguagePack current;
            lock (Sync)
            {
                current = _current;
            }

            var value = current.Find(key) ?? Neutral.Find(key);
            if (value != null)
            {
                return value;
            }

            var message = "缺少本地化文字：" + key;
            Trace.TraceError(message);
            Debug.Fail(message);
            return "!" + key + "!";
        }

        /// <summary>
        /// 取一条带占位符的运行时文字并填入参数。
        /// <para>
        /// 用位置占位符而不是字符串拼接：拼接顺序在英语里往往不成立（中文的
        /// 「<c>{0}的相关编辑</c>」对应英语是「<c>Edit "{0}"</c>」）。
        /// </para>
        /// </summary>
        public static string T(string key, params object[] args)
        {
            var format = T(key);
            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                // InvariantCulture：这里格式化的是界面文字模板，不应随线程区域设置改变
                // 数字与日期的呈现。
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                // 译文里的占位符和调用方传的参数不匹配。属于资源文件的错误，
                // 报告出来但不要让界面崩掉。
                var message = "本地化文字的占位符与参数不匹配：" + key;
                Trace.TraceError(message);
                Debug.Fail(message);
                return format;
            }
        }

        /// <summary>
        /// 查一条文字，键不存在时返回 <c>null</c>。
        /// <para>
        /// 供 <see cref="LocalizationApplier"/> 使用：设计器拥有的控件文字缺键是正常
        /// 情况（简体中文就靠设计器里的字面量），不是需要诊断的错误。
        /// </para>
        /// </summary>
        public static string Find(string key)
        {
            LanguagePack current;
            lock (Sync)
            {
                current = _current;
            }

            return current.Find(key) ?? Neutral.Find(key);
        }

        /// <summary>
        /// 任一受支持语言的资源集里是否存在该键。
        /// <para>
        /// <see cref="Find"/> 只能回答「当前语言有没有」。应用器需要区分的是另一件事：
        /// 这个控件到底受不受资源管辖。英语资源里有键而当前是简体中文，意味着「应回退到
        /// 设计器原文」；所有语言都没有键，才意味着「这个控件不该被碰」。
        /// </para>
        /// </summary>
        public static bool IsKnownKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (Neutral.Find(key) != null)
            {
                return true;
            }

            foreach (var pack in AllPacks)
            {
                if (pack.Find(key) != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
