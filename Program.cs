using System;
using System.Windows.Forms;

namespace iPhoneSyncAgent
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Проверяем, не запущен ли уже экземпляр
            using (var mutex = new System.Threading.Mutex(false, "iPhoneSyncAgent_Unique_Mutex"))
            {
                if (!mutex.WaitOne(0, false))
                {
                    MessageBox.Show("iPhoneSync Agent уже запущен!", "Предупреждение", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                Application.Run(new Form1());
            }
        }
    }
}
