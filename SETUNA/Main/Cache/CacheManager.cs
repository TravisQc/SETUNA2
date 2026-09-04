using System;
using System.Collections.Generic;
using System.IO;

namespace SETUNA.Main.Cache
{
    public class CacheManager : IScrapAddedListener, IScrapRemovedListener, IScrapLocationChangedListener, IScrapImageChangedListener, IScrapStyleAppliedListener, IScrapStyleRemovedListener
    {
        /// <summary>
        /// 缓存根目录。默认是用户本地应用数据目录下的 SETUNA 子目录，
        /// 可通过 <see cref="SetRoot"/> 改写，使测试不必读写用户真实数据。
        /// </summary>
        public static string Path { private set; get; } = DefaultRoot;

        public static readonly CacheManager Instance = new CacheManager();


        public bool IsInit { private set; get; }


        static string DefaultRoot => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SETUNA");

        /// <summary>
        /// 改写缓存根目录。传入 null 或空串则恢复默认位置。
        /// </summary>
        public static void SetRoot(string root)
        {
            Path = string.IsNullOrEmpty(root) ? DefaultRoot : root;
        }

        public void Init()
        {
            Init(null);
        }

        public void Init(string root)
        {
            SetRoot(root);

            IsInit = false;
            var scrapBook = Mainform.Instance.scrapBook;
            scrapBook.addScrapAddedListener(this);
            scrapBook.addScrapRemovedListener(this);

            RestoreScraps(scrapBook);
        }

        void RestoreScraps(ScrapBook mainBook)
        {
            var directoryInfo = new DirectoryInfo(Path);
            if (!directoryInfo.Exists)
            {
                directoryInfo.Create();
            }

            var directories = directoryInfo.GetDirectories("*", SearchOption.TopDirectoryOnly);
            var list = new List<CacheItem>(directories.Length);
            foreach (var directory in directories)
            {
                var item = CacheItem.Read(directory.FullName);
                if ((item?.IsValid ?? false) == false)
                {
                    continue;
                }

                list.Add(item);
            }

            list.Sort((x, y) => x.SortingOrder.CompareTo(y.SortingOrder));

            // RestoreChain 保证：每项只推进一次、单项失败跳过继续、
            // 结束时 IsInit 一定被置位。
            RestoreChain.Run(
                list.Count,
                (index, advance) => mainBook.AddScrapFromCache(list[index], advance),
                () => IsInit = true);
        }


        void IScrapAddedListener.ScrapAdded(object sender, ScrapEventArgs e)
        {
            var scrap = e.scrap;

            // 已经绑定缓存则忽略
            if (scrap.CacheItem != null)
            {
                return;
            }

            var style = new Style
            {
                ID = scrap.StyleID,
                ClickPoint = scrap.StyleClickPoint
            };

            var cacheItem = CacheItem.Create(scrap.DateTime, scrap.Image, scrap.Location, style);
            scrap.CacheItem = cacheItem;
        }

        void IScrapRemovedListener.ScrapRemoved(object sender, ScrapEventArgs e)
        {
            var scrap = e.scrap;
            var cacheItem = scrap?.CacheItem;
            if (cacheItem == null)
            {
                return;
            }

            scrap.CacheItem = null;
            cacheItem.Delete();
        }

        void IScrapLocationChangedListener.ScrapLocationChanged(object sender, ScrapEventArgs e)
        {
            var scrap = e.scrap;
            var cacheItem = scrap?.CacheItem;
            if (cacheItem == null)
            {
                return;
            }

            cacheItem.Position = scrap.Location;
            cacheItem.SaveInfo();
        }

        void IScrapImageChangedListener.ScrapImageChanged(object sender, ScrapEventArgs e)
        {
            var scrap = e.scrap;
            var cacheItem = scrap?.CacheItem;
            var image = scrap?.Image;
            if (cacheItem == null || image == null)
            {
                return;
            }

            cacheItem.SaveImage(image);
        }

        void IScrapStyleAppliedListener.ScrapStyleApplied(object sender, ScrapEventArgs e)
        {
            var scrap = e.scrap;
            var styleID = scrap?.StyleID ?? 0;
            var cacheItem = scrap?.CacheItem;
            if (cacheItem == null || styleID == 0)
            {
                return;
            }

            cacheItem.Style = new Style(styleID, scrap.StyleClickPoint);
            cacheItem.SaveInfo();
        }

        void IScrapStyleRemovedListener.ScrapStyleRemoved(object sender, ScrapEventArgs e)
        {
            var scrap = e.scrap;
            var cacheItem = scrap?.CacheItem;
            if (cacheItem == null)
            {
                return;
            }

            cacheItem.Style = new Style(0, new System.Drawing.Point(0, 0));
            cacheItem.SaveInfo();
        }
    }
}