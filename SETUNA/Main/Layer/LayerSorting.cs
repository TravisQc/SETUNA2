using System.Collections.Generic;

namespace SETUNA.Main.Layer
{
    /// <summary>
    /// 层级排序值的压缩算法。不依赖 <see cref="LayerManager"/> 的实例状态，便于直接验证。
    /// </summary>
    public static class LayerSorting
    {
        /// <summary>
        /// 把 <paramref name="forms"/> 的排序值就地压缩为从 0 开始的连续序号，
        /// 并返回压缩后的最大排序值（空集合返回 -1）。
        /// 原本排序值相同的项，压缩后排序值仍然相同。
        /// </summary>
        public static int Compact(IList<FormData> forms)
        {
            if (forms == null || forms.Count == 0)
            {
                return -1;
            }

            var sorted = new List<FormData>(forms);
            sorted.Sort((x, y) => x.SortingOrder.CompareTo(y.SortingOrder));

            // 单次扫描：排序值每变化一次，序号才递增一次，因此相同排序值得到相同序号。
            // 比较用的是覆写前的原始排序值，所以就地覆写是安全的。
            var order = 0;
            var previousSortingOrder = sorted[0].SortingOrder;

            foreach (var item in sorted)
            {
                if (item.SortingOrder != previousSortingOrder)
                {
                    previousSortingOrder = item.SortingOrder;
                    order++;
                }

                item.SortingOrder = order;
            }

            return order;
        }
    }
}
