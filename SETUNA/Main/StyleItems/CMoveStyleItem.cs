using System;
using System.Drawing;
using SETUNA.Main.Localization;

namespace SETUNA.Main.StyleItems
{
    using SETUNA.Properties;

    // Token: 0x02000062 RID: 98
    public class CMoveStyleItem : CStyleItem
    {
        // Token: 0x17000085 RID: 133
        // (get) Token: 0x06000373 RID: 883 RVA: 0x00015DCA File Offset: 0x00013FCA
        // (set) Token: 0x06000372 RID: 882 RVA: 0x00015D97 File Offset: 0x00013F97
        public int Top
        {
            get => _top;
            set
            {
                if (value > CMoveStyleItem.MAX_MOVE)
                {
                    _top = CMoveStyleItem.MAX_MOVE;
                    return;
                }
                if (value < -CMoveStyleItem.MAX_MOVE)
                {
                    _top = -CMoveStyleItem.MAX_MOVE;
                    return;
                }
                _top = value;
            }
        }

        // Token: 0x17000086 RID: 134
        // (get) Token: 0x06000375 RID: 885 RVA: 0x00015E05 File Offset: 0x00014005
        // (set) Token: 0x06000374 RID: 884 RVA: 0x00015DD2 File Offset: 0x00013FD2
        public int Left
        {
            get => _left;
            set
            {
                if (value > CMoveStyleItem.MAX_MOVE)
                {
                    _left = CMoveStyleItem.MAX_MOVE;
                    return;
                }
                if (value < -CMoveStyleItem.MAX_MOVE)
                {
                    _left = -CMoveStyleItem.MAX_MOVE;
                    return;
                }
                _left = value;
            }
        }

        // Token: 0x06000376 RID: 886 RVA: 0x00015E0D File Offset: 0x0001400D
        public CMoveStyleItem()
        {
            Top = 0;
            Left = 0;
        }

        // Token: 0x06000377 RID: 887 RVA: 0x00015E23 File Offset: 0x00014023
        public override string GetName()
        {
            return "Move";
        }

        // Token: 0x06000378 RID: 888 RVA: 0x00015E2A File Offset: 0x0001402A
        public override string GetDisplayName()
        {
            return Lang.T("StyleItem.Move.DisplayName");
        }

        // Token: 0x06000379 RID: 889 RVA: 0x00015E31 File Offset: 0x00014031
        public override string GetDescription()
        {
            return Lang.T("StyleItem.Move.Description");
        }

        // Token: 0x0600037A RID: 890 RVA: 0x00015E38 File Offset: 0x00014038
        public override void Apply(ref ScrapBase scrap, Point clickpoint)
        {
            var targetLocation = new Point(scrap.TargetLocation.X, scrap.TargetLocation.Y);
            targetLocation.X += Left;
            targetLocation.Y += Top;
            scrap.TargetLocation = targetLocation;
        }

        // Token: 0x0600037B RID: 891 RVA: 0x00015E9A File Offset: 0x0001409A
        protected override ToolBoxForm GetToolBoxForm()
        {
            return new MoveStyleItemPanel(this);
        }

        // Token: 0x0600037C RID: 892 RVA: 0x00015EA4 File Offset: 0x000140A4
        protected override void SetTunedStyleItem(CStyleItem newOwn)
        {
            var cmoveStyleItem = (CMoveStyleItem)newOwn;
            Top = cmoveStyleItem.Top;
            Left = cmoveStyleItem.Left;
        }

        // Token: 0x17000087 RID: 135
        // (get) Token: 0x0600037D RID: 893 RVA: 0x00015ED0 File Offset: 0x000140D0
        public override string StateText
        {
            get
            {
                // 每个方向是「标签 + 像素值 + 单位」，整段才是可翻译单位，所以用带
                // 位置占位符的模板而不是拼接：英语里数字和标签的顺序不一定和中文相同。
                var text = "";
                if (_top < 0)
                {
                    text += Lang.T("StyleItem.Move.StateUp", Math.Abs(_top));
                }
                if (_top > 0)
                {
                    text += Lang.T("StyleItem.Move.StateDown", Math.Abs(_top));
                }
                if (_left < 0)
                {
                    text += Lang.T("StyleItem.Move.StateLeft", Math.Abs(_left));
                }
                if (_left > 0)
                {
                    text += Lang.T("StyleItem.Move.StateRight", Math.Abs(_left));
                }
                return text;
            }
        }

        // Token: 0x0600037E RID: 894 RVA: 0x00015FFA File Offset: 0x000141FA
        public override Bitmap GetIcon()
        {
            return Resources.Icon_Move;
        }

        // Token: 0x040001F0 RID: 496
        public static int MAX_MOVE = 1000;

        // Token: 0x040001F1 RID: 497
        protected int _top;

        // Token: 0x040001F2 RID: 498
        protected int _left;
    }
}
