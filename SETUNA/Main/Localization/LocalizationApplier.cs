using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace SETUNA.Main.Localization
{
    /// <summary>
    /// 把语言资源应用到一棵控件树。
    /// <para>
    /// 存在的理由是不去改动 <c>*.Designer.cs</c>：把 <c>Text = "截取"</c> 改成
    /// <c>Text = Lang.T(...)</c> 会让 VS 设计器无法在设计视图求值，而且下一次有人在
    /// 设计器里拖动控件时 <c>InitializeComponent</c> 会被重新生成，那些调用可能被吞掉。
    /// </para>
    /// <para>
    /// 覆盖是**按键驱动**的：只有资源集里显式存在对应键时才写入。显示用户数据的控件
    /// （参考图名、自动操作名列表等）没有键，因此不会被触碰——默认行为是不动，这比维护
    /// 一份排除名单可靠。
    /// </para>
    /// </summary>
    internal static class LocalizationApplier
    {
        /// <summary>窗体自身文字的键后缀，沿用 WinForms 本地化资源里的同名约定。</summary>
        private const string SelfKey = "$this";

        private const string ToolTipSuffix = ".ToolTip";
        private const string ItemsInfix = ".Items.";

        /// <summary>
        /// 每个控件第一次被应用之前的原始文字，也就是设计器给的那份简体中文。
        /// <para>
        /// 这是简体中文对设计器文字的**唯一**来源，中性资源集里没有这些键。切换到英语会
        /// 覆盖控件文字，再切回简体中文时如果只是「查不到键就不动」，控件就永远停在英语上
        /// ——所以必须先把原文记下来，回退时还原。
        /// </para>
        /// <para>
        /// 用 <see cref="ConditionalWeakTable{TKey,TValue}"/> 而不是字典：键是控件实例，
        /// 弱引用保证窗体关闭后这里不会把它们留住。
        /// </para>
        /// </summary>
        private static readonly ConditionalWeakTable<Control, OriginalText> Originals =
            new ConditionalWeakTable<Control, OriginalText>();

        /// <summary>一个控件的原始文字快照。</summary>
        private sealed class OriginalText
        {
            public string Text;
            public string[] Items;
            public string ToolTip;
        }

        /// <summary>
        /// 对 <paramref name="root"/> 及其后代应用当前语言。
        /// </summary>
        public static void Apply(Control root)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }

            // 作用域是「声明了这段设计器文字的类型」。控件可能由基类的设计器声明
            // （<c>ToolBoxForm</c> 的 cmdOK/cmdCancel 就被 19 个面板继承），所以查找时
            // 要沿本程序集内的基类链逐级尝试，而不是只用 root 的实际类型名。
            var scopes = ScopeChain(root.GetType());
            if (scopes.Count == 0)
            {
                return;
            }

            ApplyToControl(root, scopes, isRoot: true);
            ApplyToolTips(root, scopes);
        }

        /// <summary>
        /// 候选作用域名：实际类型，然后沿基类链向上，直到离开本程序集。
        /// <para>
        /// 不按「嵌套的自定义控件各自开新作用域」来划分：本项目的自定义控件多数是
        /// <c>HotkeyControl</c>、<c>SetunaListBox</c> 这类叶子控件，它们的文字与工具提示
        /// 是由**宿主窗体**的设计器设置的，键也就落在宿主窗体名下。按嵌套开新作用域反而
        /// 会把这些键找丢。
        /// </para>
        /// </summary>
        private static List<string> ScopeChain(Type type)
        {
            var scopes = new List<string>();
            for (var current = type; current != null && IsOwnType(current); current = current.BaseType)
            {
                scopes.Add(current.Name);
            }

            return scopes;
        }

        private static bool IsOwnType(Type type)
        {
            return type != null && ReferenceEquals(type.Assembly, typeof(LocalizationApplier).Assembly);
        }

        /// <summary>在候选作用域里依次查找 <paramref name="suffix"/>，返回首个命中。</summary>
        private static string Find(List<string> scopes, string suffix)
        {
            for (var i = 0; i < scopes.Count; i++)
            {
                var text = Lang.Find(scopes[i] + "." + suffix);
                if (text != null)
                {
                    return text;
                }
            }

            return null;
        }

        private static void ApplyToControl(Control control, List<string> scopes, bool isRoot)
        {
            if (control == null || control.IsDisposed)
            {
                return;
            }

            // 作用域根（窗体本身）用 $this，其余用控件名。
            var name = isRoot ? SelfKey : control.Name;
            if (!string.IsNullOrEmpty(name))
            {
                // 「这个控件是否由资源管辖」用「任一语言里存在该键」判定，而不是「当前语言
                // 里存在」：只有前者才能区分「英语没翻译，回退到设计器原文」和「这个控件
                // 压根不该被本地化」（显示用户数据的标签、输入框等）。
                if (IsLocalizable(scopes, name))
                {
                    var snapshot = SnapshotOf(control);
                    if (snapshot.Text == null)
                    {
                        snapshot.Text = control.Text ?? string.Empty;
                    }

                    control.Text = Find(scopes, name) ?? snapshot.Text;
                }

                ApplyListItems(control, scopes, name);
            }

            ApplyColumnHeaders(control, scopes);
            ApplyToolStripOf(control, scopes);

            foreach (Control child in control.Controls)
            {
                ApplyToControl(child, scopes, isRoot: false);
            }
        }

        /// <summary>
        /// 本地化 <see cref="ComboBox"/>/<see cref="ListBox"/> 由设计器预置的固定条目。
        /// <para>
        /// 这些条目在设计器里是 <c>Items.AddRange(new object[] { "覆盖", ... })</c>，既不是
        /// 控件也不是 <c>Text</c> 属性，机械提取时最容易漏掉。键用序号而不是原文，因为
        /// 选中值在代码里是按 <see cref="ListControl.SelectedIndex"/> 用的，序号就是这些
        /// 条目的稳定身份。
        /// </para>
        /// <para>
        /// 只在**全部**条目都能查到译文时才替换：替换会重置
        /// <see cref="ListControl.SelectedIndex"/>，所以先存后写；部分替换会让条目语言
        /// 混杂，不如整体跳过。
        /// </para>
        /// </summary>
        private static void ApplyListItems(Control control, List<string> scopes, string controlName)
        {
            IList items;
            if (control is ComboBox comboBox)
            {
                items = comboBox.Items;
            }
            else if (control is ListBox listBox)
            {
                items = listBox.Items;
            }
            else
            {
                return;
            }

            if (items.Count == 0)
            {
                return;
            }

            // 与控件文字同样的判定：只有资源里确实登记过这个下拉框的条目才碰它。
            // 装着用户数据的列表（自动操作列表等）没有键，因此不会被触碰。
            if (!IsLocalizable(scopes, controlName + ItemsInfix + "0"))
            {
                return;
            }

            var snapshot = SnapshotOf(control);
            if (snapshot.Items == null)
            {
                var original = new string[items.Count];
                for (var i = 0; i < items.Count; i++)
                {
                    if (!(items[i] is string text))
                    {
                        return;
                    }

                    original[i] = text;
                }

                snapshot.Items = original;
            }

            if (snapshot.Items.Length != items.Count)
            {
                // 条目数量在运行时被代码改过，快照对不上了。宁可不动，也不要把用户看到的
                // 列表换成一份长度不同的旧内容。
                return;
            }

            var translated = new string[snapshot.Items.Length];
            for (var i = 0; i < translated.Length; i++)
            {
                translated[i] = Find(scopes, controlName + ItemsInfix + i.ToString(CultureInfo.InvariantCulture))
                    ?? snapshot.Items[i];
            }

            var selectedIndex = GetSelectedIndex(control);

            // 整体清空后重新加入，而不是逐个赋值 items[i]：就地替换不会刷新
            // ComboBox.Text（下拉框显示的那行文字），而随后把 SelectedIndex 写回同一个
            // 值又被 WinForms 当作没有变化而跳过，结果条目已是新语言、显示的仍是旧语言。
            // Clear() 会把 SelectedIndex 置为 -1，所以下面的写回是一次真实变更，
            // 显示文字随之刷新。
            items.Clear();
            for (var i = 0; i < translated.Length; i++)
            {
                items.Add(translated[i]);
            }

            // 条目数量与快照一致，所以原索引一定仍然有效。
            SetSelectedIndex(control, selectedIndex);
        }

        /// <summary>
        /// 这个键是否由资源管辖——任一语言的资源集里存在即算。
        /// <para>
        /// 判定必须跨语言，否则无法区分「当前语言没有这条翻译，应回退到设计器原文」和
        /// 「这个控件不由资源管辖，任何语言下都不该动」。
        /// </para>
        /// </summary>
        private static bool IsLocalizable(List<string> scopes, string suffix)
        {
            for (var i = 0; i < scopes.Count; i++)
            {
                if (Lang.IsKnownKey(scopes[i] + "." + suffix))
                {
                    return true;
                }
            }

            return false;
        }

        private static OriginalText SnapshotOf(Control control)
        {
            if (!Originals.TryGetValue(control, out var snapshot))
            {
                snapshot = new OriginalText();
                Originals.Add(control, snapshot);
            }

            return snapshot;
        }

        private static int GetSelectedIndex(Control control)
        {
            if (control is ComboBox comboBox)
            {
                return comboBox.SelectedIndex;
            }

            return control is ListBox listBox ? listBox.SelectedIndex : -1;
        }

        private static void SetSelectedIndex(Control control, int index)
        {
            if (control is ComboBox comboBox)
            {
                comboBox.SelectedIndex = index;
            }
            else if (control is ListBox listBox)
            {
                listBox.SelectedIndex = index;
            }
        }

        private static void ApplyColumnHeaders(Control control, List<string> scopes)
        {
            if (!(control is ListView listView))
            {
                return;
            }

            foreach (ColumnHeader column in listView.Columns)
            {
                if (string.IsNullOrEmpty(column.Name))
                {
                    continue;
                }

                var text = Find(scopes, column.Name);
                if (text != null)
                {
                    column.Text = text;
                }
            }
        }

        /// <summary>
        /// 本地化 <see cref="ToolStrip"/> 家族的条目。<see cref="ToolStripItem"/> 不是
        /// <see cref="Control"/>，不在 <see cref="Control.Controls"/> 里，必须单独遍历。
        /// </summary>
        private static void ApplyToolStripOf(Control control, List<string> scopes)
        {
            if (control is ToolStrip toolStrip)
            {
                ApplyToolStripItems(toolStrip.Items, scopes);
            }
        }

        private static void ApplyToolStripItems(ToolStripItemCollection items, List<string> scopes)
        {
            foreach (ToolStripItem item in items)
            {
                ApplyToolStripItem(item, scopes);
            }
        }

        private static void ApplyToolStripItem(ToolStripItem item, List<string> scopes)
        {
            if (item == null || item.IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(item.Name))
            {
                var text = Find(scopes, item.Name);
                if (text != null)
                {
                    item.Text = text;
                }

                var toolTip = Find(scopes, item.Name + ToolTipSuffix);
                if (toolTip != null)
                {
                    item.ToolTipText = toolTip;
                }
            }

            if (item is ToolStripDropDownItem dropDown)
            {
                ApplyToolStripItems(dropDown.DropDownItems, scopes);
            }
        }

        /// <summary>
        /// 本地化工具提示。<see cref="ToolTip"/> 是 <see cref="System.ComponentModel.Component"/>，
        /// 不在控件树里，只能从窗体（含基类）的字段里找出来，再对有对应键的控件调用
        /// <see cref="ToolTip.SetToolTip"/>。
        /// </summary>
        private static void ApplyToolTips(Control root, List<string> scopes)
        {
            var toolTips = FindToolTips(root);
            if (toolTips.Count == 0)
            {
                return;
            }

            foreach (var toolTip in toolTips)
            {
                ApplyToolTipsToTree(toolTip, root, scopes, isRoot: true);
            }
        }

        private static void ApplyToolTipsToTree(ToolTip toolTip, Control control, List<string> scopes, bool isRoot)
        {
            if (control.IsDisposed)
            {
                return;
            }

            if (!isRoot && !string.IsNullOrEmpty(control.Name))
            {
                var suffix = control.Name + ToolTipSuffix;
                if (IsLocalizable(scopes, suffix))
                {
                    var snapshot = SnapshotOf(control);
                    if (snapshot.ToolTip == null)
                    {
                        snapshot.ToolTip = toolTip.GetToolTip(control) ?? string.Empty;
                    }

                    toolTip.SetToolTip(control, Find(scopes, suffix) ?? snapshot.ToolTip);
                }
            }

            foreach (Control child in control.Controls)
            {
                ApplyToolTipsToTree(toolTip, child, scopes, isRoot: false);
            }
        }

        private static List<ToolTip> FindToolTips(Control root)
        {
            var result = new List<ToolTip>();
            for (var type = root.GetType(); type != null && IsOwnType(type); type = type.BaseType)
            {
                var fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (typeof(ToolTip).IsAssignableFrom(field.FieldType) && field.GetValue(root) is ToolTip toolTip)
                    {
                        result.Add(toolTip);
                    }
                }
            }

            return result;
        }
    }
}
