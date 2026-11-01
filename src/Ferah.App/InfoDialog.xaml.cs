using System.Windows;
using System.Windows.Media;

namespace Ferah.App;

public partial class InfoDialog : Window
{
    public InfoDialog(string heading, string message, bool isWarning)
    {
        InitializeComponent();
        WindowTheme.EnableDarkTitleBar(this);
        DataContext = new InfoDialogModel(heading, message, isWarning);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

public sealed record InfoDialogModel(string Heading, string Message, bool IsWarning)
{
    public string Icon => IsWarning ? "\uE7BA" : "\uE946";
    public Brush IconColor => new SolidColorBrush(IsWarning ? Color.FromRgb(232, 191, 121) : Color.FromRgb(101, 226, 190));
    public Brush IconBackground => new SolidColorBrush(IsWarning ? Color.FromRgb(55, 45, 31) : Color.FromRgb(31, 61, 57));
}
