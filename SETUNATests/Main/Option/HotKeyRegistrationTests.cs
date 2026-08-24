using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;
using SETUNA.Main.Option;

namespace SETUNA.Main.Option.Tests
{
    /// <summary>
    /// Pins the three-state registration result. The previous <c>bool</c> conflated
    /// "not enabled" with "registered", which is what let a single conflicting
    /// hotkey switch off the user's global-hotkey setting for all of them.
    /// <para>
    /// A real <c>RegisterHotKey</c> call needs a window handle, so the
    /// <c>Registered</c> path and the conflict path belong to the manual checklist;
    /// what is asserted here is that a disabled or unconfigured hotkey is reported
    /// as <c>NotEnabled</c> rather than as success or failure.
    /// </para>
    /// </summary>
    [TestClass]
    public class HotKeyRegistrationTests
    {
        [TestMethod]
        public void AnUnconfiguredHotKeyIsReportedAsNotEnabled()
        {
            var option = SetunaOption.GetDefaultOption();
            option.ScrapHotKeyEnable = true;
            option.ScrapHotKeys[(int)HotKeyID.Capture] = System.Windows.Forms.Keys.None;

            var result = option.RegistHotKey(IntPtr.Zero, HotKeyID.Capture);

            Assert.AreEqual(HotKeyRegistrationResult.NotEnabled, result);
        }

        [TestMethod]
        public void HotKeysDisabledByTheUserAreReportedAsNotEnabled()
        {
            var option = SetunaOption.GetDefaultOption();
            option.ScrapHotKeyEnable = false;

            for (var hotKeyId = HotKeyID.Capture; hotKeyId < HotKeyID.__Count__; hotKeyId++)
            {
                Assert.AreEqual(
                    HotKeyRegistrationResult.NotEnabled,
                    option.RegistHotKey(IntPtr.Zero, hotKeyId),
                    "Hotkey " + hotKeyId + " is switched off, not failing.");
            }
        }

        [TestMethod]
        public void RegistrationNeverMutatesTheUsersHotKeyEnableSetting()
        {
            // The regression being locked down: registration outcomes must not
            // write back to a persisted user preference.
            var option = SetunaOption.GetDefaultOption();
            option.ScrapHotKeyEnable = true;
            option.ScrapHotKeys[(int)HotKeyID.Capture] = System.Windows.Forms.Keys.None;

            option.RegistHotKey(IntPtr.Zero, HotKeyID.Capture);
            option.RegistHotKey(IntPtr.Zero, HotKeyID.Function1);

            Assert.IsTrue(option.ScrapHotKeyEnable);
        }

        [TestMethod]
        public void EveryRealHotKeyIdStaysInsideTheKeyArray()
        {
            // RegisterHotKeys iterates Capture..__Count__ rather than
            // Enum.GetValues precisely because __Count__ would index out of range.
            var option = SetunaOption.GetDefaultOption();

            Assert.AreEqual((int)HotKeyID.__Count__, option.ScrapHotKeys.Length);

            for (var hotKeyId = HotKeyID.Capture; hotKeyId < HotKeyID.__Count__; hotKeyId++)
            {
                Assert.IsTrue((int)hotKeyId < option.ScrapHotKeys.Length);
            }
        }

        [TestMethod]
        public void RegisteringTheSentinelIdWouldOverrunTheKeyArray()
        {
            // Documents why the loop bound matters: if someone switches
            // RegisterHotKeys to Enum.GetValues, this is what happens.
            var option = SetunaOption.GetDefaultOption();

            Assert.ThrowsException<IndexOutOfRangeException>(
                () => option.RegistHotKey(IntPtr.Zero, HotKeyID.__Count__));
        }
    }
}
