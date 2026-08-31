using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// A snapshot of one control tree's layout: how far each control reaches past its
    /// container, and how much sibling controls overlap.
    /// <para>
    /// Shared by the translation tests and the DPI-relayout tests because both guard the
    /// same failure: a control grows (a longer caption, a larger font) and is either
    /// pushed out of its group box or painted over by a neighbour. Both are silent — the
    /// text is laid out correctly and then covered — so the measurement has to exist in
    /// exactly one place.
    /// </para>
    /// </summary>
    sealed class LayoutSnapshot
    {
        /// <summary>
        /// An <c>AutoSize</c> control's width includes a few pixels of padding after the
        /// last glyph, so an overlap or overflow of that order costs no visible text.
        /// Anything larger eats into the glyphs themselves.
        /// </summary>
        public const int SlopPixels = 2;

        /// <summary>Pixels by which a control reaches past its container's right edge.</summary>
        public readonly Dictionary<string, int> Overflow = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Shared pixels per pair of siblings, keyed by both their paths.</summary>
        public readonly Dictionary<string, int> Overlap = new Dictionary<string, int>(StringComparer.Ordinal);

        public readonly Dictionary<string, string> Text = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Pixels the control's caption actually occupies, measured with its own font.
        /// <para>
        /// The height is the sharp signal for a DPI change: it is the realised pixel em of
        /// the font, a small integer, and it exposes a font realised one pixel too large —
        /// which the reported <c>Font.Height</c> does not, because that can come out
        /// identical while the rendered text is 10% wider. The width is the coarse signal:
        /// every glyph advance is an integer too, so a short caption cannot scale by an
        /// arbitrary ratio exactly.
        /// </para>
        /// </summary>
        public readonly Dictionary<string, Size> TextSize = new Dictionary<string, Size>(StringComparer.Ordinal);

        /// <summary>
        /// Measures <paramref name="container"/> and everything below it into this
        /// snapshot. Call it more than once with different roots to accumulate several
        /// windows into one snapshot.
        /// </summary>
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

                Text[childPath] = child.Text ?? string.Empty;
                Overflow[childPath] = child.Right - container.ClientSize.Width;

                if (!string.IsNullOrEmpty(child.Text))
                {
                    TextSize[childPath] = TextRenderer.MeasureText(child.Text, child.Font);
                }

                for (var j = i + 1; j < children.Count; j++)
                {
                    var other = children[j];
                    var shared = Rectangle.Intersect(child.Bounds, other.Bounds);
                    if (!shared.IsEmpty)
                    {
                        // The smaller dimension is what a caption actually loses: two
                        // controls side by side overlap over their full height, and it is
                        // the horizontal bite that hides text.
                        Overlap[childPath + " / " + other.Name] = Math.Min(shared.Width, shared.Height);
                    }
                }

                Capture(child, childPath);
            }
        }

        /// <summary>
        /// Controls that reach further past their container than they did in
        /// <paramref name="baseline"/>. Inherited slop is not a regression: the designer's
        /// coordinates already carry a few pixels of it (a checkbox whose right edge sits
        /// a hair outside its group box, a label butting against a spin box), and
        /// reporting that would bury the real regressions.
        /// </summary>
        public List<Growth> OverflowGrowth(LayoutSnapshot baseline)
        {
            var grown = new List<Growth>();

            foreach (var pair in Overflow)
            {
                int before;
                if (!baseline.Overflow.TryGetValue(pair.Key, out before))
                {
                    continue;
                }

                // Only growth counts. A control already outside its container in the
                // baseline is inherited, not caused by the change under test.
                if (pair.Value > Math.Max(before, 0) + SlopPixels)
                {
                    grown.Add(new Growth(pair.Key, before, pair.Value));
                }
            }

            return grown;
        }

        /// <summary>
        /// Sibling pairs that overlap by more than they did in
        /// <paramref name="baseline"/>. Controls that already share space in the baseline
        /// are doing so by design (the tab pages all sit on top of each other, for one).
        /// </summary>
        public List<Growth> OverlapGrowth(LayoutSnapshot baseline)
        {
            var grown = new List<Growth>();

            foreach (var pair in Overlap)
            {
                int before;
                baseline.Overlap.TryGetValue(pair.Key, out before);

                if (pair.Value > before + SlopPixels)
                {
                    grown.Add(new Growth(pair.Key, before, pair.Value));
                }
            }

            return grown;
        }

        /// <summary>
        /// One control (or pair of controls) that takes more room than it did in the
        /// baseline. Callers format their own message: what "before" and "after" mean —
        /// another language, another DPI — is theirs to explain.
        /// </summary>
        public sealed class Growth
        {
            public Growth(string path, int before, int after)
            {
                Path = path;
                Before = before;
                After = after;
            }

            public readonly string Path;
            public readonly int Before;
            public readonly int After;
        }

        /// <summary>
        /// Drives a form far enough for <c>OnLoad</c> and the first layout pass to have
        /// run, off the side of every monitor so no window appears for the length of the
        /// suite.
        /// <para>
        /// Deliberately not minimized, which is how the rest of the suite hides its
        /// windows: a minimized form reports an empty client area, and docked panels
        /// inside it collapse to nothing, so the containers these tests measure against
        /// would all be zero-sized.
        /// </para>
        /// </summary>
        public static void ShowOffScreen(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            form.Hide();
        }
    }
}
