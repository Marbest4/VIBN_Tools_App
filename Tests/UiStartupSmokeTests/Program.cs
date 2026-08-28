using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VIBN_Tools.Application;
using VIBN_Tools.Application.View;
using VIBN_Tools.Application.VM;
using VIBN_Tools.Core.Kanbanize;
using VIBN_Tools.Core.ViCo;
using VIBN_Tools.ContainerToFeeVisual;
using VIBN_Tools.Settings;
using VIBN_Tools.Tia.Contracts;

namespace VIBN_Tools.UiStartup.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        _ = new System.Windows.Application();
        var bindingTrace = PresentationTraceSources.DataBindingSource;
        var bindingErrors = new BindingErrorTraceListener();
        bindingTrace.Switch.Level = SourceLevels.Error;
        bindingTrace.Listeners.Add(bindingErrors);

        try
        {
            var workspacePage = new ViCoWorkspacePage();
            ExerciseDeferredTemplates(workspacePage);
            var feeVersionInfo = new FeeVersionInfoProvider().Read();
            if (string.Equals(feeVersionInfo.UsedSdkVersion, "Nicht erkannt", StringComparison.Ordinal))
                throw new InvalidOperationException("The FEE SDK used by the running build must be visible in Project Settings.");
            var projectPage = new ViCoPage();
            var projectViewModel = (ViCoPageVM)projectPage.DataContext;
            projectViewModel.Projects.Add(new ProjectLocation("GM1234/05-130", @"C:\Projects\GM1234\05-130"));

            var searchPage = new ViCoSearchPage();
            var searchViewModel = (ViCoSearchPageVM)searchPage.DataContext;
            var workstation = new ViCoWorkstation(
                "GM12345 Tool PC",
                "GM12345",
                "zkds-simulation-p01",
                "TIA Portal V19 | Beckhoff TwinCAT 3",
                "FEE 5",
                "LAN Industrial",
                new[] { "[W] GM1234/05-130 Demo" },
                new[] { "[W] GM1234/05-130 Demo", "TIA Portal V19", "Beckhoff TwinCAT 3", "Robot: R01 – In Arbeit" },
                new[]
                {
                    new AutomationSoftwareInfo(AutomationPlatform.SiemensTiaPortal, "TIA Portal V19", "TIA Portal V19"),
                    new AutomationSoftwareInfo(AutomationPlatform.BeckhoffTwinCat, "Beckhoff TwinCAT 3", "Beckhoff TwinCAT 3")
                },
                new[] { new ViCoRobotInfo("R01", "In Arbeit", "Robot card") },
                new ViCoWorkstationConfiguration(
                    710,
                    new ViCoConfigurationField("USER", "zkds-simulation-p01", 711),
                    new ViCoConfigurationField("STANDORT", "Werk 2", 712),
                    new ViCoConfigurationField("SW", "TIA V19 / Beckhoff TwinCAT 3", 713),
                    new ViCoConfigurationField("PROJEKT-IP", "10.20.30.40", 714),
                    new ViCoConfigurationField("SONSTIGES", "Testdaten für die Anleitung", 715)));
            var workstationRow = new ViCoWorkstationRowVM(workstation);
            workstationRow.SetOnline(true);
            workstationRow.SetRemoteSession(new ViCoRemoteSessionInfo(
                true,
                "grob\\operator",
                "grob\\operator",
                new DateTimeOffset(2026, 8, 25, 8, 30, 0, TimeSpan.Zero)));
            searchViewModel.Results.Add(workstationRow);
            searchViewModel.SelectedWorkstation = workstationRow;

            var administrationPage = new ViCoAdministrationPage();
            var administrationViewModel = (ViCoAdministrationPageVM)administrationPage.DataContext;
            administrationViewModel.RoleEntries.Add(new ViCoUserRole(@"grob\user", "Level9", "test"));

            var specialDevicePage = new SpecialDevicePage();
            var specialDeviceViewModel = (SpecialDevicePageVM)specialDevicePage.DataContext;
            specialDeviceViewModel.TiaHardwareRows.Add(new TiaHardwareDeviceRowVM(
                new TiaHardwareModuleInfo
                {
                    Slot = 3,
                    DeviceName = "PLC_1",
                    ModuleName = "Cognex Testmodul",
                    ModuleType = "PROFINET IO device",
                    TypeIdentifier = "TEST-COGNEX",
                    FirmwareVersion = "V2.1",
                    InputStartByte = 20,
                    InputLength = 4,
                    OutputStartByte = 40,
                    OutputLength = 4
                }));
            VerifyTiaHardwareMappingPersistence();

            var visualPlanService = VerifyContainerToFeeVisualPlan();
            var visualContainerViewModel = new ContainerToFeeVisualPageVM(visualPlanService);
            visualContainerViewModel.SelectedTreeNode = visualContainerViewModel.TreeRoots
                .SelectMany(root => root.SelfAndDescendants())
                .First(node => node.Kind == VisualNodeKind.Container);
            var visualContainerPage = new ContainerToFeeVisualPage
            {
                DataContext = visualContainerViewModel
            };

            var kanbanizeCardPage = new KanbanizeCardPage();
            var kanbanizeViewModel = (KanbanizeCardPageVM)kanbanizeCardPage.DataContext;
            // Populate the deferred DataGrid template as well: this catches
            // bindings in the coloured synchronization preview before release.
            var sourceCard = new KanbanizeCardInfo(
                4711,
                1392,
                1,
                2,
                "[VIBN] Grundinbetriebnahme UI-Prüfung",
                null,
                DateTimeOffset.UtcNow);
            var schedule = new VibnWorkplaceSchedule(
                DateTimeOffset.UtcNow.AddDays(-14),
                DateTimeOffset.UtcNow.AddDays(56));
            var createRow = new VibnWorkplaceSynchronizationRowVM(
                new VibnWorkplaceSynchronizationItem(
                    VibnWorkplaceSynchronizationAction.Create,
                    sourceCard,
                    null,
                    "UI-Prüfdatensatz ohne externen Schreibzugriff.",
                    schedule));
            var deadlineRow = new VibnWorkplaceSynchronizationRowVM(
                new VibnWorkplaceSynchronizationItem(
                    VibnWorkplaceSynchronizationAction.UpdateDeadline,
                    sourceCard with { Id = 4712 },
                    sourceCard with { Id = 5712, BoardId = 1541 },
                    "UI-Prüfung einer vorhandenen Karte.",
                    schedule));
            if (!createRow.IsSelected || deadlineRow.IsSelected)
                throw new InvalidOperationException("Only new Kanbanize cards must be selected by default.");

            kanbanizeViewModel.WorkplaceSynchronization.PreviewItems.Add(createRow);
            kanbanizeViewModel.WorkplaceSynchronization.PreviewItems.Add(deadlineRow);
            kanbanizeViewModel.WorkplaceSynchronization.SelectAllCommand.Execute(null);
            if (kanbanizeViewModel.WorkplaceSynchronization.PreviewItems.Any(item => item.CanSynchronize && !item.IsSelected))
                throw new InvalidOperationException("Selecting all Kanbanize preview rows failed.");
            kanbanizeViewModel.WorkplaceSynchronization.DeselectAllCommand.Execute(null);
            if (kanbanizeViewModel.WorkplaceSynchronization.PreviewItems.Any(item => item.IsSelected))
                throw new InvalidOperationException("Deselecting all Kanbanize preview rows failed.");
            // Restore the documented initial state for the generated handbook preview.
            createRow.IsSelected = true;

            FrameworkElement[] integratedViews =
            [
                projectPage,
                searchPage,
                new ViCoCopyPage(),
                new TiaPortalPage(),
                administrationPage,
                kanbanizeCardPage,
                specialDevicePage,
                visualContainerPage,
                new DiagnosticsPanel()
            ];

            foreach (var view in integratedViews)
            {
                if (view.DataContext is null)
                    throw new InvalidOperationException($"{view.GetType().Name} has no view model.");
                ExerciseDeferredTemplates(view);
            }

            // Manual form, queue and TIA hardware grid now share one page.
            // A populated row catches its ComboBox and converter bindings.
            ExerciseDeferredTemplates(specialDevicePage);

            if (Environment.GetEnvironmentVariable("VIBN_CAPTURE_UI_PREVIEW") == "1")
            {
                SavePreview(searchPage, Path.Combine(AppContext.BaseDirectory, "vico-search-preview.png"));
                SavePreview(projectPage, Path.Combine(AppContext.BaseDirectory, "vico-projects-preview.png"));
                SavePreview(workspacePage, Path.Combine(AppContext.BaseDirectory, "vico-workspace-preview.png"));
                SavePreview(kanbanizeCardPage, Path.Combine(AppContext.BaseDirectory, "kanbanize-cards-preview.png"));
                SavePreview(specialDevicePage, Path.Combine(AppContext.BaseDirectory, "special-devices-preview.png"));
                SavePreview(visualContainerPage, Path.Combine(AppContext.BaseDirectory, "container2fee-visual-preview.png"));
            }

            Dispatcher.CurrentDispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);

            if (bindingErrors.Messages.Count > 0)
            {
                throw new InvalidOperationException(
                    "WPF binding errors were detected:" + Environment.NewLine +
                    string.Join(Environment.NewLine, bindingErrors.Messages));
            }

            Console.WriteLine("All integrated WPF views initialized without binding errors.");
            return 0;
        }
        finally
        {
            bindingTrace.Listeners.Remove(bindingErrors);
        }
    }

    private static void ExerciseDeferredTemplates(FrameworkElement view)
    {
        var size = new Size(1600, 900);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();

        foreach (var dataGrid in FindVisualChildren<DataGrid>(view))
        {
            if (dataGrid.Items.Count == 0)
                continue;
            dataGrid.SelectedIndex = 0;
            dataGrid.ScrollIntoView(dataGrid.Items[0]);
            dataGrid.UpdateLayout();
        }

        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
    }

    private static void VerifyTiaHardwareMappingPersistence()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"vibn-tia-hardware-mapping-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonTiaHardwareMappingStore(path);
            var expected = new TiaHardwareMapping(
                "Device|pn-device|Module/Slot3|3|1",
                true,
                "SafeCoupler",
                62,
                70,
                "Grob",
                "SafePnPn",
                string.Empty);
            store.SaveAsync(new[] { expected }).GetAwaiter().GetResult();
            var restored = store.LoadAsync().GetAwaiter().GetResult();
            if (!restored.TryGetValue(expected.Key, out var actual) || actual != expected)
                throw new InvalidOperationException("Persisted TIA hardware mapping was not restored unchanged.");

            var legacyFirstArea = new TiaHardwareDeviceRowVM(new TiaHardwareModuleInfo
            {
                DeviceName = "Device",
                ProfinetName = "pn-device",
                ModulePath = "Module/Slot3",
                Slot = 3,
                Subslot = 1,
                AddressSetIndex = 0,
                InputStartByte = 62,
                InputLengthBits = 96,
                InputLength = 12,
                OutputStartByte = 62,
                OutputLengthBits = 48,
                OutputLength = 6,
            });
            var secondArea = new TiaHardwareDeviceRowVM(new TiaHardwareModuleInfo
            {
                DeviceName = "Device",
                ProfinetName = "pn-device",
                ModulePath = "Module/Slot3",
                Slot = 3,
                Subslot = 1,
                AddressSetIndex = 1,
                InputStartByte = 74,
                InputLengthBits = 48,
                InputLength = 6,
                OutputStartByte = 68,
                OutputLengthBits = 96,
                OutputLength = 12,
            });
            if (!legacyFirstArea.ApplyMapping(expected) || secondArea.ApplyMapping(expected))
                throw new InvalidOperationException("Legacy mapping keys must migrate only to the first address area.");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static ContainerToFeeVisualPlanService VerifyContainerToFeeVisualPlan()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"vibn-container2fee-visual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var xmlPath = Path.Combine(directory, "Container.xml");
        try
        {
            File.WriteAllText(
                xmlPath,
                """
                <AutoCreate>
                  <Container id="sensor-1">
                    <Component>Sensor_1</Component>
                    <Type>Sensor</Type>
                    <Entries>
                      <Entry>
                        <Slot>PLC_IN_PartPresent_Ch1</Slot>
                        <Signal>Sensor_1_Present</Signal>
                        <Address>%I10.0</Address>
                        <DataType>Bool</DataType>
                        <ID>1</ID>
                      </Entry>
                    </Entries>
                  </Container>
                </AutoCreate>
                """);

            var service = new ContainerToFeeVisualPlanService();
            var loaded = service.LoadXmlAsync(xmlPath).GetAwaiter().GetResult();
            if (!loaded.Success || loaded.Plan is null)
                throw new InvalidOperationException($"Visual plan could not be loaded: {loaded.Message}");
            if (loaded.Plan.Targets.Count != 1 ||
                loaded.Plan.Nodes.Count(node => node.Kind == VisualNodeKind.Signal) != 1 ||
                loaded.Plan.Edges.Count == 0)
                throw new InvalidOperationException("Visual plan does not contain the expected target, signal and edges.");

            var container = loaded.Plan.Nodes.Single(node => node.Kind == VisualNodeKind.Container);
            if (!container.SupportsCreation || !service.SetCreationRequested(container.Id, true))
                throw new InvalidOperationException("Visual creation request could not be enabled.");
            if (!service.Undo() || service.CurrentPlan!.IsCreationRequested(container.Id))
                throw new InvalidOperationException("Visual creation-request undo failed.");
            if (!service.Redo() || !service.CurrentPlan!.IsCreationRequested(container.Id))
                throw new InvalidOperationException("Visual creation-request redo failed.");

            service.SaveSidecarAsync().GetAwaiter().GetResult();
            var restored = new ContainerToFeeVisualPlanService();
            var restoredResult = restored.LoadSidecarAsync(service.CurrentPlan.SidecarPath)
                .GetAwaiter()
                .GetResult();
            if (!restoredResult.Success ||
                restored.CurrentPlan?.IsCreationRequested(container.Id) != true)
                throw new InvalidOperationException("Visual sidecar was not restored correctly.");

            return restored;
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static void SavePreview(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32);
        var canvas = new DrawingVisual();
        using (var drawing = canvas.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1600, 900));
            drawing.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, 1600, 900));
        }
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class BindingErrorTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Messages.Add(message);
        }

        public override void WriteLine(string? message) => Write(message);
    }
}
