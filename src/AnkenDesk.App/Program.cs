using System;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppServices services;
            try
            {
                services = new AppServices();
            }
            catch (Exception ex)
            {
                MessageBox.Show("DBを開けませんでした。\r\n\r\n" + ex.Message, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (services)
            {
                services.EnsureDefaultWorkspaceExists();
                Application.Run(new MainForm(services));
            }
        }
    }
}
