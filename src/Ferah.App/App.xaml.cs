using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace Ferah.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("tr-TR")));
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            var dialog = new InfoDialog("Ferah · İşlem hatası",
                "Beklenmeyen bir hata oluştu. İşlemi yeniden denemeden önce durumu kontrol edin.\n\n" + args.Exception.Message,
                isWarning: true);
            if (MainWindow is { IsVisible: true } owner) dialog.Owner = owner;
            dialog.ShowDialog();
            args.Handled = true;
        };
        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
    }
}
