using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Layer.Tests
{
    [TestClass]
    public class LayerSortingTests
    {
        [TestMethod]
        public void EmptyInputReportsNoMaximum()
        {
            Assert.AreEqual(-1, LayerSorting.Compact(new List<FormData>()));
            Assert.AreEqual(-1, LayerSorting.Compact(null));
        }

        [TestMethod]
        public void SingleItemIsCompactedToZero()
        {
            var forms = Build(42);

            Assert.AreEqual(0, LayerSorting.Compact(forms));
            Assert.AreEqual(0, forms[0].SortingOrder);
        }

        [TestMethod]
        public void IdenticalOrdersCollapseToASingleValue()
        {
            var forms = Build(7, 7, 7, 7);

            Assert.AreEqual(0, LayerSorting.Compact(forms));
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, Orders(forms));
        }

        [TestMethod]
        public void AlreadyContiguousOrdersAreUnchanged()
        {
            var forms = Build(0, 1, 2, 3, 4);

            Assert.AreEqual(4, LayerSorting.Compact(forms));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, Orders(forms));
        }

        [TestMethod]
        public void LargeGapsAreClosed()
        {
            var forms = Build(10, 5000, 999999);

            Assert.AreEqual(2, LayerSorting.Compact(forms));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Orders(forms));
        }

        [TestMethod]
        public void NegativeOrdersAreCompactedFromZero()
        {
            var forms = Build(-300, -1, 0, 8);

            Assert.AreEqual(3, LayerSorting.Compact(forms));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Orders(forms));
        }

        [TestMethod]
        public void DuplicatesKeepASharedOrderAndDoNotConsumeExtraSlots()
        {
            // 5 个不同排序值 -> 压缩后为 0..4；原本相同的项压缩后仍然相同。
            var forms = Build(3, 3, 9, 20, 20, 20, 41, 77);

            Assert.AreEqual(4, LayerSorting.Compact(forms));
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 2, 2, 3, 4 }, Orders(forms));
        }

        [TestMethod]
        public void InputOrderDoesNotAffectTheResult()
        {
            // 与上一个用例相同的多重集合，只是输入顺序被打乱。
            var forms = Build(77, 20, 3, 41, 20, 9, 3, 20);

            Assert.AreEqual(4, LayerSorting.Compact(forms));
            CollectionAssert.AreEqual(new[] { 4, 2, 0, 3, 2, 1, 0, 2 }, Orders(forms));
        }

        [TestMethod]
        public void CompactionPreservesRelativeOrderAndIsContiguous()
        {
            // 数百量级的确定输入：每个排序值重复 3 次，并留出大间隙。
            const int DistinctCount = 200;

            var original = new List<int>();
            for (var i = 0; i < DistinctCount; i++)
            {
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    original.Add(i * 500 - 10000);
                }
            }

            var forms = Build(original.ToArray());
            var maxSortingOrder = LayerSorting.Compact(forms);

            Assert.AreEqual(DistinctCount - 1, maxSortingOrder);
            Assert.AreEqual(maxSortingOrder, forms.Max(x => x.SortingOrder));
            Assert.AreEqual(0, forms.Min(x => x.SortingOrder));

            // 压缩后的值恰好覆盖 0..maxSortingOrder，不留空洞。
            CollectionAssert.AreEqual(
                Enumerable.Range(0, DistinctCount).ToArray(),
                forms.Select(x => x.SortingOrder).Distinct().OrderBy(x => x).ToArray());

            // 原始值的相等/大小关系被完整保留。
            for (var i = 0; i < forms.Count; i++)
            {
                for (var j = i + 1; j < forms.Count; j++)
                {
                    var expected = original[i].CompareTo(original[j]);
                    var actual = forms[i].SortingOrder.CompareTo(forms[j].SortingOrder);
                    Assert.AreEqual(expected, actual, "序号 {0} 与 {1} 的相对关系改变了", i, j);
                }
            }
        }

        [TestMethod]
        public void CompactionIsIdempotent()
        {
            var forms = Build(3, 3, 9, 20, 20, 41);

            var first = LayerSorting.Compact(forms);
            var firstOrders = Orders(forms);

            var second = LayerSorting.Compact(forms);

            Assert.AreEqual(first, second);
            CollectionAssert.AreEqual(firstOrders, Orders(forms));
        }

        static List<FormData> Build(params int[] sortingOrders)
        {
            return sortingOrders.Select(x => new FormData(null, x)).ToList();
        }

        static int[] Orders(IEnumerable<FormData> forms)
        {
            return forms.Select(x => x.SortingOrder).ToArray();
        }
    }
}
