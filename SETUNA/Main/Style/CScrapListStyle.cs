using System.Windows.Forms;

using SETUNA.Main.Localization;

namespace SETUNA.Main.Style
{
    // Token: 0x02000047 RID: 71
    public class CScrapListStyle : CPreStyle
    {
        // Token: 0x060002BA RID: 698 RVA: 0x0000F4CE File Offset: 0x0000D6CE
        public CScrapListStyle()
        {
            _styleid = -6;
            _stylename = Lang.T("Style.ScrapList.Name");
        }

        // Token: 0x060002BB RID: 699 RVA: 0x0000F4E9 File Offset: 0x0000D6E9
        public override ToolStripItem GetToolStrip(ScrapBook scrapbook)
        {
            return new ToolStripScrapList(base.GetDisplayName(), scrapbook);
        }
    }
}
