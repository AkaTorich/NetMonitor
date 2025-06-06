using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RDPLoginMonitor
{
    class Program
    {
        [STAThread]
        static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                System.Windows.Forms.Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Критическая ошибка: {ex.Message}", "Ошибка",
                               System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }
    }
}
