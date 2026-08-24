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
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineWidth < MainWindowGeometry.MaximumWidth);
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineHeight < MainWindowGeometry.MaximumHeight);

            // Windows' own minimum track size on a 175% display is 236x64; the
            // configured minimum has to exceed it to have any effect there.
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineWidth > 236, "The minimum width must beat the system floor at 175%.");
            Assert.IsTrue(MainWindowGeometry.MinimumBaselineHeight > 64, "The minimum height must beat the system floor at 175%.");
        }

        [TestMethod]
        public void ScaledMinimumStaysInsideTheFixedMaximumAtEveryCommonScale()
        {
            // 96 = 100%, 120 = 125%, 168 = 175%, 240 = 250%, 288 = 300%. The
            // maximum is a plain pixel value that WinForms never rescales, so the
            // scaled minimum has to stay under it on every monitor.
            foreach (var dpi in new[] { 96, 120, 144, 168, 192, 240, 288 })
            {
                var minimum = MainWindowGeometry.ScaleMinimum(dpi);

                Assert.IsTrue(minimum.Width < MainWindowGeometry.MaximumWidth, "Minimum width exceeds the maximum at " + dpi + " DPI.");
                Assert.IsTrue(minimum.Height < MainWindowGeometry.MaximumHeight, "Minimum height exceeds the maximum at " + dpi + " DPI.");
            }
        }

        [TestMethod]
        public void ScaledMinimumReturnsTheMeasuredBaselineAtItsOwnDpi()
        {
            var baseline = MainWindowGeometry.ScaleMinimum(MainWindowGeometry.MinimumBaselineDpi);

            Assert.AreEqual(MainWindowGeometry.MinimumBaselineWidth, baseline.Width);
            Assert.AreEqual(MainWindowGeometry.MinimumBaselineHeight, baseline.Height);
        }

        [TestMethod]
        public void ScaledMinimumShrinksOnLowerDpiAndGrowsOnHigherDpi()
        {
            var low = MainWindowGeometry.ScaleMinimum(96);
            var baseline = MainWindowGeometry.ScaleMinimum(MainWindowGeometry.MinimumBaselineDpi);
            var high = MainWindowGeometry.ScaleMinimum(288);

            Assert.AreEqual(148, low.Width);
            Assert.AreEqual(91, low.Height);
            Assert.IsTrue(low.Width < baseline.Width && baseline.Width < high.Width);
            Assert.IsTrue(low.Height < baseline.Height && baseline.Height < high.Height);
        }

        [TestMethod]
        public void ScaledMinimumIsSkippedForANonPositiveDpi()
        {
            // Size.Empty tells the caller to leave the current MinimumSize alone
            // rather than collapsing the window to nothing.
            Assert.IsTrue(MainWindowGeometry.ScaleMinimum(0).IsEmpty);
            Assert.IsTrue(MainWindowGeometry.ScaleMinimum(-96).IsEmpty);
        }

        [TestMethod]
        public void ScaledMinimumLeavesTheDefaultSizeReachable()
        {
            // Chrome sizes measured on this machine: 16x39 at 96 DPI and 24x64 at
            // 168 DPI. The default is a client size, so the reachable outer
            // default is the client default plus that chrome.
            AssertDefaultReachable(96, 16, 39);
            AssertDefaultReachable(MainWindowGeometry.MinimumBaselineDpi, 24, 64);
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
            var minimum = MainWindowGeometry.ScaleMinimum(MainWindowGeometry.MinimumBaselineDpi);

            var clamped = MainWindowGeometry.Clamp(9999, 9999, minimum);

            Assert.AreEqual(MainWindowGeometry.MaximumWidth, clamped.Width);
            Assert.AreEqual(MainWindowGeometry.MaximumHeight, clamped.Height);
        }

        [TestMethod]
        public void ClampPushesAnUndersizedPersistedSizeUpToTheScaledMinimum()
        {
            var minimum = MainWindowGeometry.ScaleMinimum(MainWindowGeometry.MinimumBaselineDpi);

            var clamped = MainWindowGeometry.Clamp(10, 10, minimum);

            Assert.AreEqual(minimum.Width, clamped.Width);
            Assert.AreEqual(minimum.Height, clamped.Height);
        }

        [TestMethod]
        public void ClampLeavesAnInRangePersistedSizeUntouched()
        {
            var minimum = MainWindowGeometry.ScaleMinimum(MainWindowGeometry.MinimumBaselineDpi);

            var clamped = MainWindowGeometry.Clamp(520, 300, minimum);

            Assert.AreEqual(520, clamped.Width);
            Assert.AreEqual(300, clamped.Height);
        }

        [TestMethod]
        public void ClampHonoursTheMinimumEvenWhenItExceedsTheMaximum()
        {
            // A hypothetical very-high-DPI monitor must never yield a size below
            // the minimum the caller asked for, even if that beats the maximum.
            var minimum = new System.Drawing.Size(MainWindowGeometry.MaximumWidth + 40, MainWindowGeometry.MaximumHeight + 40);

            var clamped = MainWindowGeometry.Clamp(100, 100, minimum);

            Assert.AreEqual(minimum.Width, clamped.Width);
            Assert.AreEqual(minimum.Height, clamped.Height);
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
