using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Common.Tests
{
    /// <summary>
    /// Pins the termination of the top-most-window search. The original loop was
    /// <c>while (!IsWindowVisible(hwnd))</c> with no null check: once
    /// <c>GetNextWindow</c> returned <see cref="IntPtr.Zero"/> at the end of the
    /// chain, <c>IsWindowVisible(Zero)</c> stayed false forever and the polling
    /// thread spun.
    /// </summary>
    [TestClass]
    public class WindowEnumerationTests
    {
        /// <summary>Builds a synthetic window chain: 1 -> 2 -> ... -> n -> Zero.</summary>
        static Func<IntPtr, IntPtr> Chain(int length)
        {
            return hwnd =>
            {
                var current = (int)hwnd;
                return current >= length ? IntPtr.Zero : new IntPtr(current + 1);
            };
        }

        static Func<IntPtr, bool> VisibleOnly(params int[] visible)
        {
            var set = new HashSet<int>(visible);
            return hwnd => set.Contains((int)hwnd);
        }

        [TestMethod]
        public void SearchStopsAtTheEndOfTheChainWhenNothingIsVisible()
        {
            var visitCount = 0;
            Func<IntPtr, bool> countingInvisible = hwnd =>
            {
                visitCount++;
                Assert.IsTrue(visitCount <= 100, "The search did not terminate.");
                return false;
            };

            var result = WindowsAPI.FindFirstVisible(new IntPtr(1), Chain(10), countingInvisible);

            Assert.AreEqual(IntPtr.Zero, result);
            Assert.AreEqual(10, visitCount, "Every window in the chain is inspected exactly once.");
        }

        [TestMethod]
        public void SearchReturnsTheFirstVisibleWindow()
        {
            var result = WindowsAPI.FindFirstVisible(new IntPtr(1), Chain(10), VisibleOnly(4, 7));

            Assert.AreEqual(new IntPtr(4), result);
        }

        [TestMethod]
        public void SearchReturnsTheHeadWhenItIsAlreadyVisible()
        {
            var getNextCalls = 0;
            Func<IntPtr, IntPtr> chain = hwnd =>
            {
                getNextCalls++;
                return Chain(10)(hwnd);
            };

            var result = WindowsAPI.FindFirstVisible(new IntPtr(1), chain, VisibleOnly(1));

            Assert.AreEqual(new IntPtr(1), result);
            Assert.AreEqual(0, getNextCalls, "A visible head needs no traversal.");
        }

        [TestMethod]
        public void SearchOnAnEmptyChainReturnsZeroWithoutProbingVisibility()
        {
            // GetTopWindow can return Zero on a session with no top-level windows.
            Func<IntPtr, bool> failIfCalled = hwnd =>
            {
                Assert.Fail("IsWindowVisible must not be probed for a null handle.");
                return false;
            };

            var result = WindowsAPI.FindFirstVisible(IntPtr.Zero, Chain(10), failIfCalled);

            Assert.AreEqual(IntPtr.Zero, result);
        }

        [TestMethod]
        public void SearchTerminatesWhenOnlyTheLastWindowIsVisible()
        {
            var result = WindowsAPI.FindFirstVisible(new IntPtr(1), Chain(10), VisibleOnly(10));

            Assert.AreEqual(new IntPtr(10), result);
        }

        [TestMethod]
        public void TheRealSearchReturnsPromptly()
        {
            // Smoke check against the live desktop: whatever it returns, it must
            // come back rather than spin.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            WindowsAPI.GetTopMostWindow();

            stopwatch.Stop();
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 2000,
                "GetTopMostWindow took " + stopwatch.ElapsedMilliseconds + " ms.");
        }
    }
}
