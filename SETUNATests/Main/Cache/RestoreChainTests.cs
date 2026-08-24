using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Cache;

namespace SETUNA.Main.Cache.Tests
{
    /// <summary>
    /// Pins the termination guarantees of the startup cache-restore chain. The
    /// original chain stalled whenever a style apply could not be started or threw:
    /// the completion callback never fired, <c>IsInit</c> stayed false, and the
    /// delayed-init timer polled forever.
    /// </summary>
    [TestClass]
    public class RestoreChainTests
    {
        [TestMethod]
        public void AnEmptyChainCompletesImmediately()
        {
            var completed = 0;

            RestoreChain.Run(0, (index, advance) => Assert.Fail("No item should be processed."), () => completed++);

            Assert.AreEqual(1, completed);
        }

        [TestMethod]
        public void EveryItemIsProcessedOnceInOrder()
        {
            var visited = new List<int>();
            var completed = 0;

            RestoreChain.Run(5, (index, advance) => { visited.Add(index); advance(); }, () => completed++);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, visited);
            Assert.AreEqual(1, completed);
        }

        [TestMethod]
        public void AThrowingItemIsSkippedAndTheChainContinues()
        {
            var visited = new List<int>();
            var completed = 0;

            RestoreChain.Run(
                5,
                (index, advance) =>
                {
                    visited.Add(index);
                    if (index == 2)
                    {
                        throw new InvalidOperationException("style apply blew up");
                    }

                    advance();
                },
                () => completed++);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, visited);
            Assert.AreEqual(1, completed, "The chain must still reach the end.");
        }

        [TestMethod]
        public void EveryItemThrowingStillCompletesTheChain()
        {
            var visited = 0;
            var completed = 0;

            RestoreChain.Run(
                4,
                (index, advance) => { visited++; throw new InvalidOperationException("nope"); },
                () => completed++);

            Assert.AreEqual(4, visited);
            Assert.AreEqual(1, completed, "IsInit must be set even when nothing could be restored.");
        }

        [TestMethod]
        public void ADoubleCallbackDoesNotSkipAnItem()
        {
            // The failure mode: advance() invoked twice for one item used to consume
            // the next cache entry without restoring it.
            var visited = new List<int>();
            var completed = 0;

            RestoreChain.Run(
                4,
                (index, advance) =>
                {
                    visited.Add(index);
                    advance();
                    advance();
                    advance();
                },
                () => completed++);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, visited);
            Assert.AreEqual(1, completed);
        }

        [TestMethod]
        public void AnItemThatThrowsAfterAdvancingIsNotProcessedTwice()
        {
            var visited = new List<int>();
            var completed = 0;

            RestoreChain.Run(
                3,
                (index, advance) =>
                {
                    visited.Add(index);
                    advance();
                    throw new InvalidOperationException("failed after advancing");
                },
                () => completed++);

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, visited);
            Assert.AreEqual(1, completed);
        }

        [TestMethod]
        public void AsynchronousAdvancingDrivesTheChainToCompletion()
        {
            // Mirrors the real flow: style application finishes on a later timer
            // tick, so advance() is called after step() has already returned.
            var pending = new Queue<Action>();
            var visited = new List<int>();
            var completed = 0;

            RestoreChain.Run(
                4,
                (index, advance) => { visited.Add(index); pending.Enqueue(advance); },
                () => completed++);

            Assert.AreEqual(1, visited.Count, "Only the first item runs before the callback arrives.");
            Assert.AreEqual(0, completed);

            while (pending.Count > 0)
            {
                pending.Dequeue()();
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, visited);
            Assert.AreEqual(1, completed);
        }

        [TestMethod]
        public void AnItemThatNeverAdvancesStallsOnlyItself()
        {
            // Documents the boundary: the chain cannot rescue a step that neither
            // advances nor throws. That is why ApplyStylesFromCache now invokes its
            // callback when it cannot start a style apply.
            var visited = new List<int>();
            var completed = 0;

            RestoreChain.Run(3, (index, advance) => visited.Add(index), () => completed++);

            CollectionAssert.AreEqual(new[] { 0 }, visited);
            Assert.AreEqual(0, completed);
        }

        [TestMethod]
        public void MixedSynchronousAndAsynchronousItemsAllComplete()
        {
            var pending = new Queue<Action>();
            var visited = new List<int>();
            var completed = 0;

            RestoreChain.Run(
                6,
                (index, advance) =>
                {
                    visited.Add(index);
                    if (index % 2 == 0)
                    {
                        advance();
                    }
                    else
                    {
                        pending.Enqueue(advance);
                    }
                },
                () => completed++);

            while (pending.Count > 0)
            {
                pending.Dequeue()();
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, visited);
            Assert.AreEqual(1, completed);
        }

        [TestMethod]
        public void ALongSynchronousChainDoesNotOverflowTheStack()
        {
            // The original implementation recursed per item; a synchronous fast-fail
            // path would have made that depth proportional to the cache size.
            var visited = 0;
            var completed = 0;

            RestoreChain.Run(50000, (index, advance) => { visited++; advance(); }, () => completed++);

            Assert.AreEqual(50000, visited);
            Assert.AreEqual(1, completed);
        }
    }
}
