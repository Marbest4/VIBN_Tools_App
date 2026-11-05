using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Linq;
using VIBN_Tools.ContainerToFeeVisual;
using VIBN_Tools.SharedWpf.Commands;

namespace VIBN_Tools.Application.VM;

public sealed partial class ContainerToFeeVisualPageVM
{
    public ICommand ClearProtocolCommand { get; private set; } = null!;
    public ICommand ShowLogicDefinitionsCommand { get; private set; } = null!;
    public ICollectionView OperationDetailsView { get; private set; } = null!;
    public IReadOnlyList<string> ProtocolFilters { get; } = ["Alle", "Offen / fehlgeschlagen", "Fehlgeschlagen"];
    private string _selectedProtocolFilter = "Alle";
    public string SelectedProtocolFilter
    {
        get => _selectedProtocolFilter;
        set { _selectedProtocolFilter = value; OnPropertyChanged(); OperationDetailsView.Refresh(); }
    }

    private void InitializeProtocol()
    {
        OperationDetailsView = CollectionViewSource.GetDefaultView(OperationDetails);
        OperationDetailsView.Filter = item => item is ContainerToFeeVisualOperationDetailVM detail &&
            (SelectedProtocolFilter == "Alle" || SelectedProtocolFilter == "Fehlgeschlagen" && detail.Severity == VisualIssueSeverity.Error ||
             SelectedProtocolFilter == "Offen / fehlgeschlagen" && detail.Severity is VisualIssueSeverity.Warning or VisualIssueSeverity.Error);
        ClearProtocolCommand = new RelayCommand(() => OperationDetails.Clear());
        ShowLogicDefinitionsCommand = new RelayCommand(ShowLogicDefinitions);
    }

    private void ShowLogicDefinitions()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Content", "LogicDefinitions", "GrobStandard");
        var planned = (_planService.CurrentPlan?.Nodes.Where(node => node.Kind == VisualNodeKind.Logic).Select(node => node.Name) ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            var lines = Directory.GetFiles(directory, "*.xml").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(path =>
            {
                var name = XDocument.Load(path).Root?.Attribute("LogicName")?.Value ?? Path.GetFileNameWithoutExtension(path);
                return $"{(planned.Contains(name) ? "[im Plan] " : "")} {name}";
            }).ToArray();
            MessageBox.Show("Bei der Generierung werden benötigte Definitionen importiert, sofern sie in FEE noch fehlen.\n\n" +
                string.Join(Environment.NewLine, lines) + "\n\nOrdner: " + directory,
                "Verfügbare GrobStandard-Logiken", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { Reject("Logikdefinitionen konnten nicht gelesen werden: " + exception.Message); }
    }
}
