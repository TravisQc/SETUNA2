using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Option;

namespace SETUNA.Main.Window.Tests
{
    /// <summary>
    /// Guards the main-window geometry bounds and the window-size persistence.
    /// <para>
    /// The assertions call <see cref="MainWindowGeometry"/> — the production source
    /// of truth for these bounds — instead of matching text in Mainform.cs, so a
    /// harmless refactor cannot turn them red and a real regression cannot stay green.
    /// Constraints that need a live <c>Form</c> (the designer's control layout, the
    /// startup call order, the resize bounds actually honoured by Windows) cannot be
    /// exercised in this host; they are carried by the <c>main-window-sizing</c> and
    /// <c>main-window-action-layout</c> specs and by the change's manual checklist.
    /// </para>
    /// </summary>
    [TestClass]
    public class MainWindowLayoutTests
    {
        /// <summary>The DPI the four baselines were originally measured at, i.e. 175%.</summary>
        const int MeasuredDpi = 168;

        [TestMethod]
        public void EveryBaselineIsDeclaredAtTheDesignersOwnDpi()
        {
            // A 168-DPI baseline is what the migration had to remove: the designer
            // declares AutoScaleDimensions = (96F, 96F), so the framework multiplies the
            // same literals by the scale factor on its own. Measured before the rebase,
            // the client area came out 726x315 at 175% instead of 415x180.
            Assert.AreEqual(DpiContext.BaseDpi, MainWindowGeometry.BaselineDpi);
        }

        [TestMethod]
        public void TheBaselinesReproduceTheMeasured175PercentGeometry()
        {
            // The numbers measured while the form was AutoScaleMode.None, i.e. raw
            // pixels on this machine's 175% monitor. Scaling the logical baselines back
            // up has to land on them, or the rebase changed the shipped window.
            Assert.AreEqual(new Size(415, 180), MainWindowGeometry.ScaleDefaultClient(MeasuredDpi));
            Assert.AreEqual(new Size(641, 361), MainWindowGeometry.ScaleMaximum(MeasuredDpi));
            Assert.AreEqual(new Size(261, 159), MainWindowGeometry.ScaleMinimum(MeasuredDpi));
        }

        [TestMethod]
        public void EveryBaselineIsAnIdentityAtTheBaselineDpi()
        {
            var dpi = MainWindowGeometry.BaselineDpi;

            Assert.AreEqual(
                new Size(MainWindowGeometry.DefaultClientWidth, MainWindowGeometry.DefaultClientHeight),
                MainWindowGeometry.ScaleDefaultClient(dpi));
            Assert.AreEqual(
                new Size(MainWindowGeometry.MinimumBaselineWidth, MainWindowGeometry.MinimumBaselineHeight),
                MainWindowGeometry.ScaleMinimum(dpi));
            Assert.AreEqual(
                new Size(MainWindowGeometry.MaximumBaselineWidth, MainWindowGeometry.MaximumBaselineHeight),
                MainWindowGeometry.ScaleMaximum(dpi));
        }

        [TestMethod]
        public void MinimumStaysBelowTheDefaultOuterSizeAndTheMaximum()
        {
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineWidth < MainWindowGeometry.MaximumBaselineWidth);
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineHeight < MainWindowGeometry.MaximumBaselineHeight);

            // Windows' own minimum track size on a 175% display is 236x64; the
            // configured minimum has to exceed it *there* to have any effect, which is a
            // statement about the scaled value now that the baseline is 96 DPI.
            var atMeasuredDpi = MainWindowGeometry.ScaleMinimum(MeasuredDpi);
            Assert.IsTrue(atMeasuredDpi.Width > 236, "The minimum width must beat the system floor at 175%.");
            Assert.IsTrue(atMeasuredDpi.Height > 64, "The minimum height must beat the system floor at 175%.");
        }

        [TestMethod]
        public void ScaledMinimumStaysInsideTheScaledMaximumAtEveryCommonScale()
        {
            // 96 = 100%, 120 = 125%, 168 = 175%, 240 = 250%, 288 = 300%. All three
            // baselines are declared at 96 DPI and scaled from there, so the ordering has
            // to hold on every monitor.
            foreach (var dpi in SupportedDpiSteps)
            {
                var minimum = MainWindowGeometry.ScaleMinimum(dpi);
                var maximum = MainWindowGeometry.ScaleMaximum(dpi);

                Assert.IsTrue(minimum.Width < maximum.Width, "Minimum width exceeds the maximum at " + dpi + " DPI.");
                Assert.IsTrue(minimum.Height < maximum.Height, "Minimum height exceeds the maximum at " + dpi + " DPI.");
            }
        }

        [TestMethod]
        public void TheDefaultOuterSizeFitsBetweenBothBoundsAtEveryCommonScale()
        {
            // The three baselines used to disagree: the default client 415x180 plus its
            // frame reached past a maximum of 366x206 at 100%, so a first launch on a
            // 100% monitor opened clamped. Chrome measured on this machine: 16x39 at 96
            // DPI, 24x64 at 168 DPI, i.e. one sixth of a logical pixel per DPI step.
            foreach (var dpi in SupportedDpiSteps)
            {
                var client = MainWindowGeometry.ScaleDefaultClient(dpi);
                var frame = new Size(16 * dpi / 96, 39 * dpi / 96);
                var outer = new Size(client.Width + frame.Width, client.Height + frame.Height);
                var minimum = MainWindowGeometry.ScaleMinimum(dpi);
                var maximum = MainWindowGeometry.ScaleMaximum(dpi);

                Assert.IsTrue(
                    minimum.Width <= outer.Width && minimum.Height <= outer.Height,
                    "The minimum blocks the default at " + dpi + " DPI: " + minimum + " > " + outer);
                Assert.IsTrue(
                    outer.Width <= maximum.Width && outer.Height <= maximum.Height,
                    "The maximum clamps the default at " + dpi + " DPI: " + outer + " > " + maximum);
            }
        }

        [TestMethod]
        public void EveryBaselineIsSkippedForAnUnusableDpi()
        {
            // Size.Empty tells the caller to leave the current bound alone rather than
            // collapsing the window to nothing — or, for the maximum, telling WinForms
            // "unbounded". DpiContext.Scale returns its input unchanged for a
            // non-positive factor, so without this guard an unavailable monitor would
            // masquerade as "the baseline is the answer".
            foreach (var dpi in new[] { 0, -96, 100000 })
            {
                Assert.IsTrue(MainWindowGeometry.ScaleMinimum(dpi).IsEmpty, dpi + " DPI");
                Assert.IsTrue(MainWindowGeometry.ScaleMaximum(dpi).IsEmpty, dpi + " DPI");
                Assert.IsTrue(MainWindowGeometry.ScaleDefaultClient(dpi).IsEmpty, dpi + " DPI");
            }
        }

        static readonly int[] SupportedDpiSteps = { 96, 120, 144, 168, 192, 240, 288 };

        [TestMethod]
        public void ClampPullsAnOversizedPersistedSizeDownToTheMaximum()
        {
            var clamped = MainWindowGeometry.Clamp(9999, 9999, Minimum(MeasuredDpi), Maximum(MeasuredDpi));

            Assert.AreEqual(Maximum(MeasuredDpi), clamped);
        }

        [TestMethod]
        public void ClampPushesAnUndersizedPersistedSizeUpToTheScaledMinimum()
        {
            var clamped = MainWindowGeometry.Clamp(10, 10, Minimum(MeasuredDpi), Maximum(MeasuredDpi));

            Assert.AreEqual(Minimum(MeasuredDpi), clamped);
        }

        [TestMethod]
        public void ClampLeavesAnInRangePersistedSizeUntouched()
        {
            var clamped = MainWindowGeometry.Clamp(520, 300, Minimum(MeasuredDpi), Maximum(MeasuredDpi));

            Assert.AreEqual(new Size(520, 300), clamped);
        }

        [TestMethod]
        public void ClampHonoursTheMinimumEvenWhenItExceedsTheMaximum()
        {
            // A persisted size restored while the bounds belong to a different DPI can
            // arrive with a minimum that beats the maximum. The result must never fall
            // below the minimum the caller asked for.
            var minimum = new Size(
                MainWindowGeometry.MaximumBaselineWidth + 40, MainWindowGeometry.MaximumBaselineHeight + 40);

            var clamped = MainWindowGeometry.Clamp(100, 100, minimum, Maximum(MainWindowGeometry.BaselineDpi));

            Assert.AreEqual(minimum, clamped);
        }

        [TestMethod]
        public void MissingOrInvalidPersistedSizeSelectsTheDefault()
        {
            Assert.IsFalse(MainWindowGeometry.HasPersistedSize(0, 0));
            Assert.IsFalse(MainWindowGeometry.HasPersistedSize(520, 0));
            Assert.IsFalse(MainWindowGeometry.HasPersistedSize(0, 300));
            Assert.IsFalse(MainWindowGeometry.HasPersistedSize(-520, -300));
            Assert.IsTrue(MainWindowGeometry.HasPersistedSize(520, 300));
        }

        [TestMethod]
        public void AnUnwrittenSizeSelectsTheDefaultWithoutADiagnostic()
        {
            var restored = Restore(0, 0, MainWindowGeometry.UnknownDpi, 96, out var diagnostic);

            Assert.IsTrue(restored.IsEmpty, "Size.Empty is how the caller is told to apply the default.");
            Assert.IsNull(diagnostic, "A first launch is not a fault and must not report one.");
        }

        [TestMethod]
        public void AMalformedSizeSelectsTheDefaultAndIsReported()
        {
            foreach (var malformed in new[] { new Size(-520, -300), new Size(520, 0), new Size(0, 300) })
            {
                var restored = Restore(malformed.Width, malformed.Height, 96, 96, out var diagnostic);

                Assert.IsTrue(restored.IsEmpty, malformed + " must not be applied.");
                Assert.IsNotNull(diagnostic, malformed + " is a corrupt value and must leave a trace.");
            }
        }

        [TestMethod]
        public void ASizeSavedOnTheSameMonitorIsRestoredUnchangedAndSilently()
        {
            var restored = Restore(300, 200, MeasuredDpi, MeasuredDpi, out var diagnostic);

            Assert.AreEqual(new Size(300, 200), restored);
            Assert.IsNull(diagnostic);
        }

        /// <summary>
        /// The <c>main-window-sizing</c> scenario: a size saved on a 175% monitor, restored
        /// on a 100% one. It has to come back as the same *visual* size, and it may not be
        /// multiplied by both DPIs.
        /// </summary>
        [TestMethod]
        public void ASizeSavedAt175PercentComesBackAsTheSameVisualSizeAt100Percent()
        {
            var saved = new Size(500, 300);

            var restored = Restore(saved.Width, saved.Height, MeasuredDpi, 96, out var diagnostic);

            // One multiplication by 96/168, rounded away from zero.
            Assert.AreEqual(new Size(286, 171), restored);
            Assert.IsNull(diagnostic, "An in-range conversion is not a fault.");

            Assert.IsTrue(restored.Width < saved.Width, "The window did not shrink for the lower-DPI monitor.");
            Assert.AreNotEqual(saved, restored, "The saved pixels were applied as-is: no conversion happened.");
            Assert.AreNotEqual(
                new Size(875, 525),
                restored,
                "The size was multiplied by the old DPI as well as divided by it.");
        }

        [TestMethod]
        public void ASizeWithoutARecordedDpiIsInterpretedOnceAgainstTheCurrentMonitorAndReported()
        {
            // What a configuration written before the DPI field looks like. There is
            // nothing to convert from, so the pixels are taken at face value once — and
            // that has to be visible in the log, because it is the one case where the
            // window can come back a different visual size than the user left it.
            var restored = Restore(300, 200, MainWindowGeometry.UnknownDpi, 96, out var diagnostic);

            Assert.AreEqual(new Size(300, 200), restored);
            Assert.IsNotNull(diagnostic);
            StringAssert.Contains(diagnostic, "no monitor DPI");
        }

        [TestMethod]
        public void ARestoredSizeOutsideTheCurrentBoundsIsClampedAndReported()
        {
            var maximum = Maximum(96);

            var restored = Restore(4000, 3000, 96, 96, out var diagnostic);

            Assert.AreEqual(maximum, restored);
            Assert.IsNotNull(diagnostic);
            StringAssert.Contains(diagnostic, "clamped");
        }

        /// <summary>
        /// The defect the recorded DPI exists to prevent. Resize on a 100% monitor, drag to
        /// 175%, close there, relaunch back on 100%: the window has to come back the size it
        /// was, not pinned to the maximum. Without the DPI there is no way to tell a window
        /// the user grew from one the monitor grew, so every crossing ratchets it up.
        /// </summary>
        [TestMethod]
        public void CrossingMonitorsAndClosingDoesNotChangeTheSizeTheUserChose()
        {
            var chosen = new Size(286, 171);

            var onTheHighDpiMonitor = Restore(chosen.Width, chosen.Height, 96, MeasuredDpi, out _);
            var backAgain = Restore(
                onTheHighDpiMonitor.Width, onTheHighDpiMonitor.Height, MeasuredDpi, 96, out _);

            Assert.AreEqual(chosen, backAgain, "A round trip across two monitors drifted.");

            // The same trip with the DPI missing is what used to happen, and it is worth
            // pinning: the assertion above would pass on an implementation that ignored
            // the saved DPI entirely if the value happened to stay in range.
            var withoutTheDpi = Restore(
                onTheHighDpiMonitor.Width,
                onTheHighDpiMonitor.Height,
                MainWindowGeometry.UnknownDpi,
                96,
                out _);

            Assert.AreEqual(
                Maximum(96),
                withoutTheDpi,
                "Precondition for the fix: read as raw pixels, a size saved at 175% hits the 100% ceiling.");
        }

        [TestMethod]
        public void AnUnusableDpiIsPersistedAsUnknownRatherThanAsAGuess()
        {
            Assert.AreEqual(MainWindowGeometry.UnknownDpi, MainWindowGeometry.PersistableDpi(0));
            Assert.AreEqual(MainWindowGeometry.UnknownDpi, MainWindowGeometry.PersistableDpi(-96));
            Assert.AreEqual(MainWindowGeometry.UnknownDpi, MainWindowGeometry.PersistableDpi(100000));
            Assert.AreEqual(168, MainWindowGeometry.PersistableDpi(168));
        }

        static Size Restore(int width, int height, int savedDpi, int currentDpi, out string diagnostic)
        {
            return MainWindowGeometry.RestoreWindowSize(
                width, height, savedDpi, currentDpi, Minimum(currentDpi), Maximum(currentDpi), out diagnostic);
        }

        static Size Minimum(int dpi)
        {
            return MainWindowGeometry.ScaleMinimum(dpi);
        }

        static Size Maximum(int dpi)
        {
            return MainWindowGeometry.ScaleMaximum(dpi);
        }

        [TestMethod]
        public void ThePersistedSizeAndItsDpiSurviveAnOptionRoundTrip()
        {
            var restored = RoundTrip(new SetunaOption
            {
                MainWindowWidth = 520,
                MainWindowHeight = 300,
                MainWindowDpi = 168
            });

            Assert.AreEqual(520, restored.MainWindowWidth);
            Assert.AreEqual(300, restored.MainWindowHeight);
            Assert.AreEqual(168, restored.MainWindowDpi);
        }

        [TestMethod]
        public void AConfigWrittenBeforeTheseFieldsDeserializesAsUnset()
        {
            var restored = RoundTrip(new SetunaOption());

            // Zero is the "no valid saved size" signal that selects the default, and
            // UnknownDpi is the "interpret once against the current monitor" signal.
            Assert.AreEqual(0, restored.MainWindowWidth);
            Assert.AreEqual(0, restored.MainWindowHeight);
            Assert.AreEqual(MainWindowGeometry.UnknownDpi, restored.MainWindowDpi);
        }

        [TestMethod]
        public void ClonePreservesThePersistedSizeAndItsDpi()
        {
            var clone = (SetunaOption)new SetunaOption
            {
                MainWindowWidth = 480,
                MainWindowHeight = 270,
                MainWindowDpi = 120
            }.Clone();

            Assert.AreEqual(480, clone.MainWindowWidth);
            Assert.AreEqual(270, clone.MainWindowHeight);
            Assert.AreEqual(120, clone.MainWindowDpi);
        }

        static SetunaOption RoundTrip(SetunaOption option)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(SetunaOption), SetunaOption.GetAllType());
            using (var buffer = new MemoryStream())
            {
                serializer.Serialize(buffer, option);
                buffer.Position = 0;
                return (SetunaOption)serializer.Deserialize(buffer);
            }
        }

        /// <summary>
        /// The designer's three size literals must be the same 96-DPI logical numbers
        /// <see cref="MainWindowGeometry"/> holds.
        /// <para>
        /// This is where the migration's silent regression lived: task 5.2 moved the form to
        /// <c>AutoScaleMode.Dpi</c> with <c>AutoScaleDimensions = (96F, 96F)</c> and left the
        /// literals at the 175% pixel values they were measured as, so the framework
        /// multiplied them a second time. It has to be a source check for the same reason as
        /// the baseline literal itself — the autoscale factor is 1 in this host, so an
        /// instance reports whatever the designer asked for — and the main window is not in
        /// <c>ApplicationForms.All()</c>, which is why no existing sweep caught it.
        /// </para>
        /// <para>
        /// The runtime overwrites both bounds in <c>Mainform_Load</c>, so a disagreement is
        /// not visible after startup either; what it changes is the window built during
        /// construction, before the first paint.
        /// </para>
        /// </summary>
        [TestMethod]
        public void TheDesignersSizeLiteralsAgreeWithTheLogicalBaseline()
        {
            var designer = File.ReadAllText(Path.Combine(
                SETUNA.Main.Runtime.Tests.RepositoryPath.FindRoot(), "SETUNA", "Mainform.Designer.cs"));

            AssertLiteral(designer, "ClientSize", MainWindowGeometry.ScaleDefaultClient(MainWindowGeometry.BaselineDpi));
            AssertLiteral(designer, "MinimumSize", MainWindowGeometry.ScaleMinimum(MainWindowGeometry.BaselineDpi));
            AssertLiteral(designer, "MaximumSize", MainWindowGeometry.ScaleMaximum(MainWindowGeometry.BaselineDpi));
        }

        static void AssertLiteral(string designer, string property, Size expected)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                designer,
                @"this\." + property + @"\s*=\s*new System\.Drawing\.Size\((?<w>-?\d+),\s*(?<h>-?\d+)\)");

            Assert.IsTrue(match.Success, "Mainform.Designer.cs no longer assigns " + property + ".");
            Assert.AreEqual(
                expected,
                new Size(int.Parse(match.Groups["w"].Value), int.Parse(match.Groups["h"].Value)),
                "The designer's " + property + " disagrees with MainWindowGeometry, so the framework"
                    + " scales a number that is already scaled.");
        }
    }
}
