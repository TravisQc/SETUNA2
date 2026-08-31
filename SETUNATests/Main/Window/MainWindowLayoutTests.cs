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
        [TestMethod]
        public void MinimumStaysBelowTheDefaultOuterSizeAndTheMaximum()
        {
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineWidth < MainWindowGeometry.MaximumBaselineWidth);
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineHeight < MainWindowGeometry.MaximumBaselineHeight);

            // Windows' own minimum track size on a 175% display is 236x64; the
            // configured minimum has to exceed it to have any effect there.
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineWidth > 236, "The minimum width must beat the system floor at 175%.");
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineHeight > 64, "The minimum height must beat the system floor at 175%.");
        }

        [TestMethod]
        public void ScaledMinimumStaysInsideTheScaledMaximumAtEveryCommonScale()
        {
            // 96 = 100%, 120 = 125%, 168 = 175%, 240 = 250%, 288 = 300%. Both bounds
            // are measured at 168 DPI and scaled from there, so the ordering has to
            // hold on every monitor.
            foreach (var dpi in new[] { 96, 120, 144, 168, 192, 240, 288 })
            {
                var minimum = MainWindowGeometry.ScaleMinimum(dpi);
                var maximum = MainWindowGeometry.ScaleMaximum(dpi);

                Assert.IsTrue(minimum.Width < maximum.Width, "Minimum width exceeds the maximum at " + dpi + " DPI.");
                Assert.IsTrue(minimum.Height < maximum.Height, "Minimum height exceeds the maximum at " + dpi + " DPI.");
            }
        }

        [TestMethod]
        public void BothScaledBoundsReturnTheMeasuredBaselineAtTheBaselineDpi()
        {
            var minimum = MainWindowGeometry.ScaleMinimum(MainWindowGeometry.BaselineDpi);
            var maximum = MainWindowGeometry.ScaleMaximum(MainWindowGeometry.BaselineDpi);

            Assert.AreEqual(MainWindowGeometry.MinimumBaselineWidth, minimum.Width);
            Assert.AreEqual(MainWindowGeometry.MinimumBaselineHeight, minimum.Height);
            Assert.AreEqual(MainWindowGeometry.MaximumBaselineWidth, maximum.Width);
            Assert.AreEqual(MainWindowGeometry.MaximumBaselineHeight, maximum.Height);
        }

        [TestMethod]
        public void BothScaledBoundsShrinkOnLowerDpiAndGrowOnHigherDpi()
        {
            var low = MainWindowGeometry.ScaleMinimum(96);
            var baseline = MainWindowGeometry.ScaleMinimum(MainWindowGeometry.BaselineDpi);
            var high = MainWindowGeometry.ScaleMinimum(288);

            Assert.AreEqual(148, low.Width);
            Assert.AreEqual(91, low.Height);
            Assert.IsTrue(low.Width < baseline.Width && baseline.Width < high.Width);
            Assert.IsTrue(low.Height < baseline.Height && baseline.Height < high.Height);

            // The maximum used to be a plain pixel value that nothing rescaled. That
            // does not survive per-monitor relayout: the layout grows with the DPI
            // while a fixed ceiling does not, so the window gets clamped and its
            // content clipped on the higher-DPI monitor.
            var maximumLow = MainWindowGeometry.ScaleMaximum(96);
            var maximumHigh = MainWindowGeometry.ScaleMaximum(288);

            Assert.AreEqual(365, maximumLow.Width);
            Assert.AreEqual(205, maximumLow.Height);
            Assert.IsTrue(maximumLow.Width < MainWindowGeometry.MaximumBaselineWidth);
            Assert.IsTrue(MainWindowGeometry.MaximumBaselineWidth < maximumHigh.Width);
        }

        [TestMethod]
        public void BothScaledBoundsAreSkippedForANonPositiveDpi()
        {
            // Size.Empty tells the caller to leave the current bound alone rather
            // than collapsing the window to nothing — or, for the maximum, telling
            // WinForms "unbounded".
            Assert.IsTrue(MainWindowGeometry.ScaleMinimum(0).IsEmpty);
            Assert.IsTrue(MainWindowGeometry.ScaleMinimum(-96).IsEmpty);
            Assert.IsTrue(MainWindowGeometry.ScaleMaximum(0).IsEmpty);
            Assert.IsTrue(MainWindowGeometry.ScaleMaximum(-96).IsEmpty);
        }

        [TestMethod]
        public void ScaledMinimumLeavesTheDefaultSizeReachable()
        {
            // Chrome sizes measured on this machine: 16x39 at 96 DPI and 24x64 at
            // 168 DPI. The default is a client size, so the reachable outer
            // default is the client default plus that chrome.
            AssertDefaultReachable(96, 16, 39);
            AssertDefaultReachable(MainWindowGeometry.BaselineDpi, 24, 64);
        }

        static void AssertDefaultReachable(int dpi, int chromeWidth, int chromeHeight)
        {
            var minimum = MainWindowGeometry.ScaleMinimum(dpi);

            Assert.IsTrue(
                minimum.Width <= MainWindowGeometry.DefaultClientWidth + chromeWidth,
                "Minimum width blocks the default at " + dpi + " DPI.");
            Assert.IsTrue(
                minimum.Height <= MainWindowGeometry.DefaultClientHeight + chromeHeight,
                "Minimum height blocks the default at " + dpi + " DPI.");
        }

        [TestMethod]
        public void ClampPullsAnOversizedPersistedSizeDownToTheMaximum()
        {
            var clamped = MainWindowGeometry.Clamp(9999, 9999, Minimum(), Maximum());

            Assert.AreEqual(MainWindowGeometry.MaximumBaselineWidth, clamped.Width);
            Assert.AreEqual(MainWindowGeometry.MaximumBaselineHeight, clamped.Height);
        }

        [TestMethod]
        public void ClampPushesAnUndersizedPersistedSizeUpToTheScaledMinimum()
        {
            var minimum = Minimum();

            var clamped = MainWindowGeometry.Clamp(10, 10, minimum, Maximum());

            Assert.AreEqual(minimum.Width, clamped.Width);
            Assert.AreEqual(minimum.Height, clamped.Height);
        }

        [TestMethod]
        public void ClampLeavesAnInRangePersistedSizeUntouched()
        {
            var clamped = MainWindowGeometry.Clamp(520, 300, Minimum(), Maximum());

            Assert.AreEqual(520, clamped.Width);
            Assert.AreEqual(300, clamped.Height);
        }

        [TestMethod]
        public void ClampHonoursTheMinimumEvenWhenItExceedsTheMaximum()
        {
            // A persisted size restored while the bounds belong to a different DPI can
            // arrive with a minimum that beats the maximum. The result must never fall
            // below the minimum the caller asked for.
            var minimum = new System.Drawing.Size(MainWindowGeometry.MaximumBaselineWidth + 40, MainWindowGeometry.MaximumBaselineHeight + 40);

            var clamped = MainWindowGeometry.Clamp(100, 100, minimum, Maximum());

            Assert.AreEqual(minimum.Width, clamped.Width);
            Assert.AreEqual(minimum.Height, clamped.Height);
        }

        static System.Drawing.Size Minimum()
        {
            return MainWindowGeometry.ScaleMinimum(MainWindowGeometry.BaselineDpi);
        }

        static System.Drawing.Size Maximum()
        {
            return MainWindowGeometry.ScaleMaximum(MainWindowGeometry.BaselineDpi);
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
        public void PersistedSizeSurvivesAnOptionRoundTrip()
        {
            var option = new SetunaOption
            {
                MainWindowWidth = 520,
                MainWindowHeight = 300
            };

            var restored = RoundTrip(option);

            Assert.AreEqual(520, restored.MainWindowWidth);
            Assert.AreEqual(300, restored.MainWindowHeight);
        }

        [TestMethod]
        public void MissingPersistedSizeDeserializesAsUnset()
        {
            var restored = RoundTrip(new SetunaOption());

            // Zero is the "no valid saved size" signal that selects the default.
            Assert.AreEqual(0, restored.MainWindowWidth);
            Assert.AreEqual(0, restored.MainWindowHeight);
        }

        [TestMethod]
        public void ClonePreservesPersistedSize()
        {
            var option = new SetunaOption
            {
                MainWindowWidth = 480,
                MainWindowHeight = 270
            };

            var clone = (SetunaOption)option.Clone();

            Assert.AreEqual(480, clone.MainWindowWidth);
            Assert.AreEqual(270, clone.MainWindowHeight);
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
    }
}
