using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Ferah.App;

public partial class MainWindow : Window, IUserInteraction, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private IntPtr _handle;
    private bool _disposed;

    public MainWindow(IReadOnlyList<string> arguments)
    {
        InitializeComponent();
        WindowTheme.EnableDarkTitleBar(this);
        _viewModel = new(this);
        DataContext = _viewModel;
        _timer = new(TimeSpan.FromSeconds(4), DispatcherPriority.Background, async (_, _) =>
        {
            try { await _viewModel.RefreshMachineAsync(); }
            catch (Exception error) { _viewModel.ReportError(error); }
        }, Dispatcher);
        Loaded += async (_, _) =>
        {
            ApplyStartupPage(arguments);
            await _viewModel.InitializeAsync();
            _timer.Start();
        };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
        };
    }

    public IntPtr WindowHandle => _handle;

    public bool Confirm(ReviewRequest request)
    {
        var dialog = new ReviewDialog(request) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Taranacak yerel klasörü seçin",
            InitialDirectory = Directory.Exists(_viewModel.ScanPath)
                ? _viewModel.ScanPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    public string? PickCsvPath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "CSV dosyasını kaydet",
            FileName = suggestedName,
            DefaultExt = ".csv",
            Filter = "CSV dosyası (*.csv)|*.csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public void Inform(string title, string message) =>
        new InfoDialog(title, message, IsWarningTitle(title)) { Owner = this }.ShowDialog();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
            Inform("İşlem sürüyor", "Dosya ve ayar güvenliğini korumak için önce Durdur düğmesine basın ve işlemin tamamlanmasını bekleyin.");
            return;
        }
        Dispose();
        base.OnClosing(e);
    }

    private void ApplyStartupPage(IReadOnlyList<string> arguments)
    {
        var first = arguments.Count == 0 ? null : arguments[0];
        var page = first?.ToLowerInvariant() switch
        {
            "--cleanup" => "Cleanup", "--large-files" => "Large", "--performance" => "Performance",
            "--history" => "History", _ => "Overview"
        };
        _viewModel.NavigateCommand.Execute(page);
    }

    private static bool IsWarningTitle(string title) =>
        title.Contains("hata", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("tamamlanamadı", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        _viewModel.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
