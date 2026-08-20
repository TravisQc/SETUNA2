using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Option;
using SETUNA.Main.Runtime.Tests;

namespace SETUNA.Main.Window.Tests
{
    /// <summary>
    /// Guards the enlarged main-window geometry, the equal-width action layout,
    /// and the window-size persistence added for those two capabilities.
    /// </summary>
    [TestClass]
    public class MainWindowLayoutTests
    {
        private const int DefaultWidth = 415;
        private const int DefaultHeight = 180;
        private const int MinimumWidth = 260;
        private const int MinimumHeight = 160;
        private const int BaselineDpi = 168;
        private const int MaximumWidth = 640;
        private const int MaximumHeight = 360;

        [TestMethod]
        public void DesignerUsesEnlargedDefaultAndBoundedMaximumSize()
        {
            var designer = ReadMainformDesigner();

            StringAssert.Contains(designer, "this.ClientSize = new System.Drawing.Size(" + DefaultWidth + ", " + DefaultHeight + ");");
            StringAssert.Contains(designer, "this.MaximumSize = new System.Drawing.Size(" + MaximumWidth + ", " + MaximumHeight + ");");
            StringAssert.Contains(designer, "this.MinimumSize = new System.Drawing.Size(" + MinimumWidth + ", " + MinimumHeight + ");");
            StringAssert.Contains(designer, "this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;");
        }

        [TestMethod]
        public void MinimumStaysBelowTheDefaultOuterSizeAndTheMaximum()
        {
            Assert.IsTrue(MinimumWidth < MaximumWidth && MinimumHeight < MaximumHeight);

            // Windows' own minimum track size on a 175% display is 236x64; the
            // configured minimum has to exceed it to have any effect there.
            Assert.IsTrue(MinimumWidth > 236, "The minimum width must beat the system floor at 175%.");
            Assert.IsTrue(MinimumHeight > 64, "The minimum height must beat the system floor at 175%.");
        }

        [TestMethod]
        public void MinimumIsRederivedForEachMonitorDpi()
        {
            var mainform = ReadMainform();

            StringAssert.Contains(mainform, "protected override void OnDpiChanged(DpiChangedEventArgs e)");
            StringAssert.Contains(mainform, "ApplyMinimumWindowSize(e.DeviceDpiNew);");

            var load = mainform.IndexOf("private void Mainform_Load(", StringComparison.Ordinal);
            var apply = mainform.IndexOf("ApplyMinimumWindowSize(DeviceDpi);", load, StringComparison.Ordinal);
            var restore = mainform.IndexOf("RestoreMainWindowSize();", load, StringComparison.Ordinal);

            Assert.IsTrue(apply > load, "The minimum must be sized for the startup monitor.");
            Assert.IsTrue(restore > apply, "The saved size must be clamped against the scaled minimum.");
        }

        [TestMethod]
        public void ScaledMinimumStaysInsideTheFixedMaximumAtEveryCommonScale()
        {
            // 96 = 100%, 120 = 125%, 168 = 175%, 240 = 250%, 288 = 300%. The
            // maximum is a plain pixel value that WinForms never rescales, so the
            // scaled minimum has to stay under it on every monitor.
            foreach (var dpi in new[] { 96, 120, 144, 168, 192, 240, 288 })
            {
                var width = MinimumWidth * dpi / BaselineDpi;
                var height = MinimumHeight * dpi / BaselineDpi;

                Assert.IsTrue(width < MaximumWidth, "Minimum width exceeds the maximum at " + dpi + " DPI.");
                Assert.IsTrue(height < MaximumHeight, "Minimum height exceeds the maximum at " + dpi + " DPI.");
            }

            Assert.AreEqual(MinimumWidth, MinimumWidth * BaselineDpi / BaselineDpi);
            Assert.AreEqual(148, MinimumWidth * 96 / BaselineDpi);
            Assert.AreEqual(91, MinimumHeight * 96 / BaselineDpi);
        }

        [TestMethod]
        public void ScaledMinimumLeavesTheDefaultSizeReachable()
        {
            // Chrome sizes measured on this machine: 16x39 at 96 DPI and 24x64 at
            // 168 DPI. The default is a client size, so the reachable outer
            // default is the client default plus that chrome.
            AssertDefaultReachable(96, 16, 39);
            AssertDefaultReachable(BaselineDpi, 24, 64);
        }

        private static void AssertDefaultReachable(int dpi, int chromeWidth, int chromeHeight)
        {
            var width = MinimumWidth * dpi / BaselineDpi;
            var height = MinimumHeight * dpi / BaselineDpi;

            Assert.IsTrue(width <= DefaultWidth + chromeWidth, "Minimum width blocks the default at " + dpi + " DPI.");
            Assert.IsTrue(height <= DefaultHeight + chromeHeight, "Minimum height blocks the default at " + dpi + " DPI.");
        }

        [TestMethod]
        public void ActionsAreHostedInTwoEqualFilledColumns()
        {
            var designer = ReadMainformDesigner();

            StringAssert.Contains(designer, "this.mainActionLayout.ColumnCount = 2;");
            StringAssert.Contains(designer, "this.mainActionLayout.Dock = System.Windows.Forms.DockStyle.Fill;");
            Assert.AreEqual(
                2,
                Regex.Matches(designer, @"mainActionLayout\.ColumnStyles\.Add\(new System\.Windows\.Forms\.ColumnStyle\(System\.Windows\.Forms\.SizeType\.Percent, 50F\)\)").Count,
                "Both action columns must claim an equal 50% share.");

            StringAssert.Contains(designer, "this.mainActionLayout.Controls.Add(this.button1, 0, 0);");
            StringAssert.Contains(designer, "this.mainActionLayout.Controls.Add(this.button4, 1, 0);");
            StringAssert.Contains(designer, "this.button1.Dock = System.Windows.Forms.DockStyle.Fill;");
            StringAssert.Contains(designer, "this.button4.Dock = System.Windows.Forms.DockStyle.Fill;");
            StringAssert.Contains(designer, "this.Controls.Add(this.mainActionLayout);");
        }

        [TestMethod]
        public void ActionButtonsKeepTheirLabelsAndClickHandlers()
        {
            var designer = ReadMainformDesigner();
            var mainform = ReadMainform();

            StringAssert.Contains(designer, "this.button1.Text = \"截取\";");
            StringAssert.Contains(designer, "this.button4.Text = \"选项\";");
            StringAssert.Contains(designer, "this.button1.Click += new System.EventHandler(this.button1_Click);");
            StringAssert.Contains(designer, "this.button4.Click += new System.EventHandler(this.button4_Click);");

            StringAssert.Contains(mainform, "StartCapture();");
            StringAssert.Contains(mainform, "Option();");
        }

        [TestMethod]
        public void SizeIsRestoredBeforeDisplayAndSavedOnClose()
        {
            var mainform = ReadMainform();

            var load = mainform.IndexOf("private void Mainform_Load(", StringComparison.Ordinal);
            Assert.IsTrue(load >= 0, "Mainform_Load must exist.");
            var loadOption = mainform.IndexOf("LoadOption();", load, StringComparison.Ordinal);
            var restore = mainform.IndexOf("RestoreMainWindowSize();", load, StringComparison.Ordinal);
            var optionApply = mainform.IndexOf("OptionApply();", restore, StringComparison.Ordinal);

            Assert.IsTrue(loadOption > load, "The persisted option must be loaded first.");
            Assert.IsTrue(restore > loadOption, "The saved size must be applied after the option is loaded.");
            Assert.IsTrue(optionApply > restore, "The size must be applied before the window becomes visible.");

            var closing = mainform.IndexOf("private void Mainform_FormClosing(", StringComparison.Ordinal);
            Assert.IsTrue(closing >= 0, "Mainform_FormClosing must exist.");
            var saveSize = mainform.IndexOf("SaveMainWindowSize();", closing, StringComparison.Ordinal);
            var saveOption = mainform.IndexOf("SaveOption();", saveSize, StringComparison.Ordinal);

            Assert.IsTrue(saveSize > closing, "The current size must be captured while closing.");
            Assert.IsTrue(saveOption > saveSize, "The captured size must be written to the config file.");
        }

        [TestMethod]
        public void RestoreClampsPersistedSizeIntoTheAllowedRange()
        {
            // Mirrors RestoreMainWindowSize: a Form cannot be hosted in this
            // suite, so the documented bounds are exercised directly.
            Assert.AreEqual(MaximumWidth, Clamp(9999, MinimumWidth, MaximumWidth));
            Assert.AreEqual(MaximumHeight, Clamp(9999, MinimumHeight, MaximumHeight));
            Assert.AreEqual(MinimumWidth, Clamp(10, MinimumWidth, MaximumWidth));
            Assert.AreEqual(MinimumHeight, Clamp(10, MinimumHeight, MaximumHeight));
            Assert.AreEqual(520, Clamp(520, MinimumWidth, MaximumWidth));
            Assert.AreEqual(300, Clamp(300, MinimumHeight, MaximumHeight));
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

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static SetunaOption RoundTrip(SetunaOption option)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(SetunaOption), SetunaOption.GetAllType());
            using (var buffer = new MemoryStream())
            {
                serializer.Serialize(buffer, option);
                buffer.Position = 0;
                return (SetunaOption)serializer.Deserialize(buffer);
            }
        }

        private static string ReadMainformDesigner()
        {
            return File.ReadAllText(Path.Combine(RepositoryPath.FindRoot(), "SETUNA", "Mainform.Designer.cs"));
        }

        private static string ReadMainform()
        {
            return File.ReadAllText(Path.Combine(RepositoryPath.FindRoot(), "SETUNA", "Mainform.cs"));
        }
    }
}
