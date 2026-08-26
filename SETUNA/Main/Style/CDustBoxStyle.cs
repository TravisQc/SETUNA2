using System.Windows.Forms;

using SETUNA.Main.Localization;

namespace SETUNA.Main.Style
{
    // Token: 0x02000048 RID: 72
    public class CDustBoxStyle : CPreStyle
    {
        // Token: 0x060002BC RID: 700 RVA: 0x0000F4F7 File Offset: 0x0000D6F7
        public CDustBoxStyle()
        {
            _styleid = -7;
            _stylename = Lang.T("Style.DustBox.Name");
        }

        // Token: 0x060002BD RID: 701 RVA: 0x0000F512 File Offset: 0x0000D712
        public override ToolStripItem GetToolStrip(ScrapBook scrapbook)
        {
            return new ToolStripDustboxList(base.GetDisplayName(), scrapbook);
        }
    }
}
