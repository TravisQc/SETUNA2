using System.Windows.Forms;

using SETUNA.Main.Localization;

namespace SETUNA.Main.Style
{
    // Token: 0x02000049 RID: 73
    public class CDustScrapStyle : CPreStyle
    {
        // Token: 0x060002BE RID: 702 RVA: 0x0000F520 File Offset: 0x0000D720
        public CDustScrapStyle()
        {
            _styleid = -12;
            _stylename = Lang.T("Style.DustScrap.Name");
        }

        // Token: 0x060002BF RID: 703 RVA: 0x0000F53B File Offset: 0x0000D73B
        public override void Apply(ref ScrapBase scrap)
        {
            Layer.LayerManager.Instance.SuspendRefresh();
            var result = MessageBox.Show(Mainform.Instance, Lang.T("Message.ConfirmCloseAllScraps"), Lang.T("Message.ConfirmCaption"), MessageBoxButtons.YesNo);
            Layer.LayerManager.Instance.ResumeRefresh();

            if (result == DialogResult.Yes)
            {
                scrap.Manager.CloseAllScrap();
            }
        }
    }
}
