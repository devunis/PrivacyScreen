using System.Windows.Forms;

namespace PrivacyScreen.Windows;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new PrivacyScreenAppContext());
    }
}
