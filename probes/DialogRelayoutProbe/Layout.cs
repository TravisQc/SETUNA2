using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DialogRelayoutProbe
{
    /// <summary>
    /// One control's measurable state: where it sits, and how large its realised font is.
    /// </summary>
    sealed class Reading
    {
        public Rectangle Bounds;
        public int FontHeight;
        public bool AutoSize;
        public bool HeightFollowsFont;
        public string Describe;

        /// <summary>
        /// True when a layout engine, not the designer, decides this control's rectangle.
        /// <see cref="AutoSize"/> is already true for these, but the round trip needs to tell
        /// the two apart: an AutoSize control still sits where the designer put it, while a
        /// flow or table panel child's position is a running total of its siblings' rounded
        /// sizes and margins.
        /// </summary>
        public bool LayoutOwned;

        /// <summary>
        /// Pixel metrics the application owns rather than the framework, or <c>null</c> for a
        /// control that owns none. Nothing else scales them, so they need checking separately
        /// from the bounds.
        /// </summary>
        public Dictionary<string, int> Metrics;
    }

    /// <summary>
    /// How far each control reaches past its container and how much siblings overlap.
    /// A deliberately small copy of the suite's LayoutSnapshot: this probe is a separate
    /// process on purpose, and giving it a project reference to the test assembly would
    /// make the test assembly a dependency of a shipped-adjacent tool.
    /// </summary>
    sealed class Layout
    {
        /// <summary>An AutoSize control carries a few pixels of padding after the last
        /// glyph, so an overflow or overlap of that order costs no visible text.</summary>
        public const int Slop = 2;

        /// <summary>
        /// Extra room allowed when the DPI actually changed. An overlap is the difference of
        /// two edges, each built from a position and a size that the framework rounds
        /// independently, and a font-driven height rounds to a whole pixel on top of that —
        /// four roundings, so up to three pixels of noise that no designer coordinate can
        /// remove. Reporting inside that band would mean nudging dozens of coordinates to
        /// silence arithmetic.
        /// </summary>
        public const int RoundingSlop = 3;

        public readonly Dictionary<string, int> Overflow = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Overlap = new Dictionary<string, int>(StringComparer.Ordinal);

        public void Capture(Control container, string path)
        {
            var children = new List<Control>();
            foreach (Control child in container.Controls)
            {
                children.Add(child);
            }

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var childPath = path + "/" + child.Name;
                Overflow[childPath] = child.Right - container.ClientSize.Width;

                for (var j = i + 1; j < children.Count; j++)
                {
                    // Sibling tab pages sit exactly on top of each other by design, so their
                    // overlap is the page size and grows with every scale-up. Their geometry
                    // is the TabControl's business, not the designer's.
                    if (child is TabPage || children[j] is TabPage)
                    {
                        continue;
                    }

                    // One control drawn entirely inside another is deliberate layering, not a
                    // collision: MoveStyleItemPanel puts each spin box inside the 73x49
                    // check box whose caption sits above it. Tracking that pair's overlap
                    // reports the design itself on every scale step.
                    var shared = Rectangle.Intersect(child.Bounds, children[j].Bounds);
                    if (!shared.IsEmpty && shared != child.Bounds && shared != children[j].Bounds)
                    {
                        Overlap[childPath + " / " + children[j].Name] =
                            Math.Min(shared.Width, shared.Height);
                    }
                }

                Capture(child, childPath);
            }
        }

        /// <summary>
        /// Controls that reach further past their container, or overlap more, than
        /// <paramref name="baseline"/> scaled by <paramref name="ratio"/>.
        /// <para>
        /// The comparison has to be proportional. Inherited slop is not a regression — the
        /// designer coordinates already carry a few pixels of it — and once a control already
        /// overflows, scaling the whole dialog up scales the overflow with it: OptionForm's
        /// <c>pictureBox1</c> reaches 311px past its container at 168 DPI and 355px at 192,
        /// which is the same picture, not a worse one.
        /// </para>
        /// </summary>
        public IEnumerable<string> RegressionsAgainst(Layout baseline, double ratio)
        {
            var allowed = Slop + (ratio == 1d ? 0 : RoundingSlop);

            foreach (var pair in Overflow)
            {
                int before;
                if (baseline.Overflow.TryGetValue(pair.Key, out before)
                    && pair.Value > Expected(Math.Max(before, 0), ratio) + allowed)
                {
                    yield return pair.Key + " reaches " + pair.Value + "px past its container (was "
                        + before + "px, so " + Expected(Math.Max(before, 0), ratio) + "px expected)";
                }
            }

            foreach (var pair in Overlap)
            {
                int before;
                baseline.Overlap.TryGetValue(pair.Key, out before);
                if (pair.Value > Expected(before, ratio) + allowed)
                {
                    yield return pair.Key + " overlaps by " + pair.Value + "px (was " + before
                        + "px, so " + Expected(before, ratio) + "px expected)";
                }
            }
        }

        static int Expected(int before, double ratio)
        {
            return (int)Math.Round(before * ratio, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// Whether each caption still fits the control that draws it.
    /// <para>
    /// The complement of <see cref="Layout"/>: that one measures where controls sit, this one
    /// measures whether the text inside them is clipped, which no amount of correct geometry
    /// guarantees. <c>AutoSize</c> controls are skipped — they grow to fit, and a grown
    /// control shows up in <see cref="Layout"/> as an overlap instead.
    /// </para>
    /// </summary>
    sealed class TextFit
    {
        /// <summary>Pixels the caption needs beyond what the control gives it; negative means slack.</summary>
        public readonly Dictionary<string, int> Shortfall = new Dictionary<string, int>(StringComparer.Ordinal);

        public readonly Dictionary<string, string> Caption = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Capture(Control container, string path, int dpi)
        {
            foreach (Control child in container.Controls)
            {
                var childPath = path + "/" + child.Name;

                if (ShouldMeasure(child))
                {
                    var available = child.Width - Chrome(child, dpi);
                    var needed = TextRenderer.MeasureText(child.Text, child.Font).Width;
                    Shortfall[childPath] = needed - available;
                    Caption[childPath] = child.Text;
                }

                Capture(child, childPath, dpi);
            }
        }

        /// <summary>
        /// Captions that fit in <paramref name="baseline"/> and do not fit here. Reported as a
        /// change rather than an absolute, because a few controls are already tight by design
        /// and listing them every run would bury the ones this DPI or language broke.
        /// </summary>
        public IEnumerable<string> NewlyClipped(TextFit baseline)
        {
            foreach (var pair in Shortfall)
            {
                int before;
                if (baseline.Shortfall.TryGetValue(pair.Key, out before) && before <= 0 && pair.Value > 0)
                {
                    yield return pair.Key + ": \"" + Caption[pair.Key] + "\" needs " + pair.Value
                        + "px more than it has (had " + (-before) + "px to spare)";
                }
            }
        }

        static bool ShouldMeasure(Control control)
        {
            if (string.IsNullOrEmpty(control.Text))
            {
                return false;
            }

            // AutoSize controls grow to fit their text, so they cannot clip it.
            if (control is Label label)
            {
                return !label.AutoSize;
            }

            if (control is CheckBox check)
            {
                return !check.AutoSize;
            }

            if (control is RadioButton radio)
            {
                return !radio.AutoSize;
            }

            if (control is Button button)
            {
                return !button.AutoSize;
            }

            // A GroupBox caption is drawn into the frame and clipped without a trace.
            return control is GroupBox;
        }

        /// <summary>
        /// Room the control spends on something other than the caption. Scaled by DPI: the
        /// check-box glyph and the button border grow with the monitor, so a constant
        /// 96-DPI figure would over-report slack at 192 and under-report it at 96.
        /// </summary>
        static int Chrome(Control control, int dpi)
        {
            var logical = control is CheckBox || control is RadioButton ? 20
                : control is Button ? 8
                : control is GroupBox ? 12
                : 2;

            return logical * dpi / 96;
        }
    }
}
