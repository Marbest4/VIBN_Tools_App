using VIBN_Tools.ContainerToFeeVisual;

namespace VIBN_Tools.ContainerGeneration.SmokeTests;

internal static class VisualXmlCorpusRegressionTests
{
    internal static async Task VerifyAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VIBN_Tools_App.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository with supplied XML examples not found.");
        var count = 0; var conflicts = 0;
        foreach (var path in Directory.GetFiles(root.FullName, "Container*.xml"))
        {
            var service = new ContainerToFeeVisualPlanService();
            var loaded = await service.LoadXmlAsync(path);
            if (loaded.Plan is not { } plan) throw new InvalidOperationException(loaded.Message);
            count += plan.Nodes.Count(node => node.Kind == VisualNodeKind.Container);
            var validation = service.Validate();
            conflicts += validation.Issues.Count(issue => issue.Code == "SIGNAL_DIRECTION_CONFLICT");
            if (validation.Issues.Any(issue => issue.Severity == VisualIssueSeverity.Error && issue.Code != "SIGNAL_DIRECTION_CONFLICT"))
                throw new InvalidOperationException($"{Path.GetFileName(path)}: unexpected plan errors: " +
                    string.Join("; ", validation.Issues.Where(issue => issue.Severity == VisualIssueSeverity.Error).Select(issue => issue.Message)));
            if (plan.Issues.Any(issue => issue.Code == "CONTAINER_TYPE_UNKNOWN" && issue.Message.Contains("GripperAddon", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Supplied gripper addon was not recognized.");
        }
        if (count != 776 || conflicts != 7) throw new InvalidOperationException($"Expected 776 containers and 7 direction conflicts; found {count} and {conflicts}.");
        Console.WriteLine("All five XML examples parsed by Container2FEEVisual; seven invalid slot directions identified.");
    }
}
