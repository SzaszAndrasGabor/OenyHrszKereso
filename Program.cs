using System.Windows.Forms;
using OenyHrszKereso;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            // Playwright telepítő mód: OenyHrszKereso.exe install chromium
            if (args.Length > 0 && args[0] == "install")
            {
                return Microsoft.Playwright.Program.Main(args);
            }

            // WinForms indítása
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Indítási hiba:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "OÉNY HRSZ Kereső – Hiba",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
