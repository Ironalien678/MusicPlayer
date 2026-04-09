using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicPlayer
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Enable high-DPI rendering so the app looks crisp
            if (Environment.OSVersion.Version.Major >= 6)
                SetProcessDPIAware();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                // Show unhandled exceptions during startup so the cause is visible
                try { MessageBox.Show("Unhandled error starting application:\n" + ex.ToString(), "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                throw;
            }
        }
    }
}
