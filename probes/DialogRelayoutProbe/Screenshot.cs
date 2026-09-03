using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using SETUNA.Main.Window;

namespace DialogRelayoutProbe
{
    /// <summary>
    /// The rendered surface of a dialog, and the one thing about it that is comparable
    /// across DPI.
    /// <para>
    /// Two screenshots of the same dialog at 96 and 192 DPI cannot be compared byte for
    /// byte — they are different sizes, and every glyph is re-hinted. What survives the
    /// scale is roughly how much of the client area is covered in ink: controls, captions
    /// and borders all grow with the window. So a dialog that lost its controls off the
    /// edge, or came up blank, shows as a collapse in coverage, while a dialog that merely
    /// re-hinted its text does not.
    /// </para>
    /// </summary>
    sealed class Screenshot
    {
        /// <summary>
        /// How far coverage may move between two DPI steps of the same dialog before it counts
        /// as ink appearing or disappearing rather than being rescaled.
        /// <para>
        /// Measured across this repository's 54 dialogs and five scale steps, the worst
        /// legitimate factor is 1.88 — antialiasing, focus rectangles and border thickness are
        /// not proportional, and a group box's caption is a fixed number of glyphs inside a box
        /// whose area grows with the square of the scale. 3.00 leaves that room and still
        /// catches a dialog that lost two thirds of its ink. Finer differences are the measured
        /// bounds' and the text-fit pass's job; this one answers "is it still drawing itself".
        /// The run prints the worst observed factor so the two can be compared.
        /// </para>
        /// </summary>
        const double CoverageTolerance = 3d;

        /// <summary>
        /// A client area with less ink than this is blank for practical purposes, which makes
        /// every comparison against it vacuous.
        /// </summary>
        const double BlankCoverage = 0.002d;

        public Size ClientSize;

        public double Coverage;

        /// <summary>How many client areas this run rendered, so the log shows the pass ran at all.</summary>
        public static int Rendered { get; private set; }

        static double lowestCoverage = double.MaxValue;

        static double highestCoverage;

        static double widestRatio = 1d;

        /// <summary>
        /// The coverage range across the whole run, and how far the worst dialog's coverage
        /// moved between two DPI steps. Printed rather than asserted: what it is for is to make
        /// a vacuous pass visible — a run whose renders were all blank, or all identical, says
        /// so here, and the observed worst ratio is what <see cref="CoverageTolerance"/> has to
        /// stay above.
        /// </summary>
        public static string DescribeCoverageRange()
        {
            return Rendered == 0
                ? "no renders"
                : Describe(lowestCoverage) + " to " + Describe(highestCoverage)
                    + ", worst cross-DPI factor " + widestRatio.ToString("F2")
                    + " (tolerance " + CoverageTolerance.ToString("F2") + ")";
        }

        /// <summary>
        /// Renders <paramref name="form"/> through <c>DrawToBitmap</c> — the same call the
        /// application uses to hand a scrap to the clipboard — and optionally writes the PNG
        /// for a human to look at.
        /// </summary>
        public static Screenshot Capture(Form form, string directory, string fileName)
        {
            var size = form.ClientSize;
            var shot = new Screenshot { ClientSize = size };

            using (var rendered = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
            {
                form.DrawToBitmap(rendered, new Rectangle(Point.Empty, size));
                shot.Coverage = InkCoverage(rendered);

                Rendered++;
                lowestCoverage = Math.Min(lowestCoverage, shot.Coverage);
                highestCoverage = Math.Max(highestCoverage, shot.Coverage);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    rendered.Save(Path.Combine(directory, fileName + ".png"), ImageFormat.Png);
                }
            }

            return shot;
        }

        /// <summary>
        /// Everything the client area is checked for at one DPI. Returns findings, empty when
        /// the dialog rendered as expected.
        /// </summary>
        public IEnumerable<string> RegressionsAgainst(Screenshot baseline, int dpi)
        {
            if (Coverage < BlankCoverage)
            {
                yield return "rendered essentially blank at " + dpi + " DPI (" + Describe(Coverage)
                    + " of the client area carries ink), so nothing about its appearance can be compared";
                yield break;
            }

            if (baseline.Coverage < BlankCoverage)
            {
                // Reported against the baseline's own DPI by the caller's first pass; nothing
                // to compare here.
                yield break;
            }

            var ratio = Coverage / baseline.Coverage;
            widestRatio = Math.Max(widestRatio, Math.Max(ratio, 1d / ratio));

            if (ratio > CoverageTolerance || ratio < 1d / CoverageTolerance)
            {
                yield return "ink coverage went from " + Describe(baseline.Coverage) + " to "
                    + Describe(Coverage) + " at " + dpi + " DPI, a factor of " + ratio.ToString("F2")
                    + " — controls have appeared or gone missing rather than being rescaled";
            }
        }

        /// <summary>
        /// The fraction of sampled pixels that are not the most common colour.
        /// <para>
        /// The mode, not the top-left pixel: measured that way first, every dialog reported
        /// 97-100% coverage, because the corner pixel is a one-pixel border shade rather than
        /// the panel behind everything — a metric pinned at its ceiling cannot detect
        /// anything. Against the mode, coverage is the ink a reader would call ink: captions,
        /// borders, buttons and images.
        /// </para>
        /// <para>
        /// Sampled on a grid rather than read whole: <c>OptionForm</c> at 192 DPI is over four
        /// million pixels and there are hundreds of renders per run, while the mode of a
        /// dialog background is not a close race.
        /// </para>
        /// </summary>
        static double InkCoverage(Bitmap bitmap)
        {
            const int TargetSamples = 200000;

            var data = bitmap.LockBits(
                new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var step = 1 + (int)Math.Sqrt((double)data.Width * data.Height / TargetSamples);
                var stride = Math.Abs(data.Stride);
                var row = new byte[stride];
                var histogram = new Dictionary<int, int>();
                var samples = 0;

                for (var y = 0; y < data.Height; y += step)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, stride);

                    for (var x = 0; x < data.Width; x += step)
                    {
                        var offset = x * 3;
                        if (offset + 2 >= stride)
                        {
                            break;
                        }

                        var colour = (row[offset] << 16) | (row[offset + 1] << 8) | row[offset + 2];
                        histogram.TryGetValue(colour, out var seen);
                        histogram[colour] = seen + 1;
                        samples++;
                    }
                }

                if (samples == 0)
                {
                    return 0d;
                }

                var background = 0;
                foreach (var entry in histogram)
                {
                    if (entry.Value > background)
                    {
                        background = entry.Value;
                    }
                }

                return (double)(samples - background) / samples;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        static string Describe(double coverage)
        {
            return (coverage * 100d).ToString("F2") + "%";
        }
    }
}
