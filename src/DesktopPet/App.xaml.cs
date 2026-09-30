using System.IO;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace DesktopPet;

public partial class App : WpfApplication
{
    private PetWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _window = new PetWindow();
            MainWindow = _window;
            _window.Closed += (_, _) => Shutdown();
            _window.Show();
        }
        catch (InvalidDataException error)
        {
            System.Windows.MessageBox.Show(error.Message, "Desktop Pet", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
