using System;
using System.Windows.Forms;
using com.clearunit;

namespace SETUNA
{
    // Token: 0x02000086 RID: 134
    internal static class Program
    {
        // Token: 0x0600046B RID: 1131 RVA: 0x0001CA08 File Offset: 0x0001AC08
        [STAThread]
        private static void Main(string[] args)
        {
            var instance = SingletonApplication.GetInstance(Application.ProductVersion, args);
            if (instance.Register())
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var mainform = new Mainform();
                instance.AddSingletonFormListener(mainform);
                mainform.CommandRun(args);
                Application.Run(mainform);
            }
        }
    }
}
