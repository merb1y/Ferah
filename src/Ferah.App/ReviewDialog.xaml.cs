using System.Windows;

namespace Ferah.App;

public partial class ReviewDialog : Window
{
    public ReviewDialog(ReviewRequest request)
    {
        InitializeComponent();
        WindowTheme.EnableDarkTitleBar(this);
        DataContext = new ReviewDialogModel(request);
        ConfirmButton.Style = (Style)FindResource(request.Destructive ? "DangerButton" : "PrimaryButton");
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

public sealed class ReviewDialogModel : ObservableObject
{
    private bool _acknowledged;
    public ReviewDialogModel(ReviewRequest request)
    {
        Title = request.Title;
        Description = request.Description;
        Warning = request.Warning;
        ConfirmText = request.ConfirmText;
        Acknowledgement = request.Acknowledgement;
        Rows = request.Rows.Take(500).ToArray();
        var totalRows = request.TotalRows ?? request.Rows.Count;
        IsTruncated = totalRows > Rows.Count;
        TruncatedText = IsTruncated ? $"Performans için ilk {Rows.Count} öğe gösteriliyor; onay toplam {totalRows:N0} öğeyi kapsar." : "";
    }
    public string Title { get; }
    public string Description { get; }
    public string Warning { get; }
    public string ConfirmText { get; }
    public string Acknowledgement { get; }
    public IReadOnlyList<ReviewRow> Rows { get; }
    public bool IsTruncated { get; }
    public string TruncatedText { get; }
    public bool Acknowledged { get => _acknowledged; set => Set(ref _acknowledged, value); }
}
