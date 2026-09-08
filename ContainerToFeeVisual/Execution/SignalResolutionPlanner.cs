using VIBN_Tools.GlobalClasses;
using VIBN_Tools.GlobalClasses.FeeObjects;

namespace VIBN_Tools.ContainerToFeeVisual;

public sealed record SignalResolutionRequest(
    string ContainerId,
    string ContainerName,
    FeeInterfaceSignal Signal);

public sealed record ExistingSignalBinding(
    SignalResolutionRequest Request,
    FeeInterfaceSignal ExistingSignal,
    FeeInterface ExistingInterface);

public sealed record MissingSignalAlias(
    SignalResolutionRequest Alias,
    SignalResolutionRequest Primary);

public sealed record SignalResolutionPlan(
    IReadOnlyList<ExistingSignalBinding> ExistingBindings,
    IReadOnlyList<SignalResolutionRequest> MissingSignals,
    IReadOnlyList<MissingSignalAlias> MissingAliases,
    IReadOnlyList<VisualIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != VisualIssueSeverity.Error);

    public void ApplyExistingBindings()
    {
        if (!IsValid)
            throw new InvalidOperationException("Ein fehlerhafter Signalplan darf nicht angewendet werden.");

        foreach (var binding in ExistingBindings)
        {
            binding.Request.Signal.Guid = binding.ExistingSignal.Guid;
            binding.Request.Signal.ParentInterface = binding.ExistingInterface;
            binding.Request.Signal.ReuseExistingWithoutUpdate = true;
        }
    }

    public void ApplyCreatedBindings(FeeInterface generationInterface)
    {
        if (!IsValid)
            throw new InvalidOperationException("Ein fehlerhafter Signalplan darf nicht angewendet werden.");

        foreach (var request in MissingSignals)
        {
            request.Signal.ParentInterface = generationInterface;
            request.Signal.ReuseExistingWithoutUpdate = true;
        }
        foreach (var alias in MissingAliases)
        {
            alias.Alias.Signal.Guid = alias.Primary.Signal.Guid;
            alias.Alias.Signal.ParentInterface = generationInterface;
            alias.Alias.Signal.ReuseExistingWithoutUpdate = true;
        }
    }
}

/// <summary>
/// Pure preflight for resolve-or-create signal handling. No SDK write occurs
/// until every requested signal is either uniquely resolved or classified as
/// missing. Missing signals are later created in the Grob Generation Interface.
/// </summary>
public static class SignalResolutionPlanner
{
    public static SignalResolutionPlan Build(
        IEnumerable<SignalResolutionRequest> requests,
        IEnumerable<FeeInterface> interfaces)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(interfaces);

        var available = interfaces
            .Where(item => item is not null)
            .SelectMany(parent => (parent.Signals ?? []).Select(signal => (Parent: parent, Signal: signal)))
            .Where(item => item.Signal.Guid != Guid.Empty)
            .ToArray();
        var bindings = new List<ExistingSignalBinding>();
        var missing = new List<SignalResolutionRequest>();
        var missingAliases = new List<MissingSignalAlias>();
        var issues = new List<VisualIssue>();

        foreach (var request in requests)
        {
            var result = FindMatches(request.Signal, available);
            if (result.Conflict is not null)
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Error,
                    result.Conflict,
                    BuildConflictMessage(request, result.Candidates),
                    request.ContainerId));
                continue;
            }

            if (result.Candidates.Length == 0)
            {
                var sameTag = string.IsNullOrWhiteSpace(request.Signal.Tag)
                    ? []
                    : missing.Where(item => string.Equals(
                            item.Signal.Tag,
                            request.Signal.Tag,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                if (sameTag.Length > 0)
                {
                    var exact = sameTag.Where(item =>
                            SameLocation(item.Signal, request.Signal) &&
                            item.Signal.IOType == request.Signal.IOType &&
                            item.Signal.Usage == request.Signal.Usage)
                        .ToArray();
                    if (exact.Length == 1)
                    {
                        missingAliases.Add(new MissingSignalAlias(request, exact[0]));
                        continue;
                    }

                    issues.Add(new VisualIssue(
                        VisualIssueSeverity.Error,
                        "NEW_SIGNAL_IDENTITY_CONFLICT",
                        $"Das neu benötigte Signal '{SignalIdentity(request.Signal)}' besitzt in mehreren " +
                        "Containern widersprüchliche Adresse, Typ- oder Nutzungsdaten.",
                        request.ContainerId));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(request.Signal.Tag))
                {
                    var sameLocation = missing.Where(item =>
                            SameLocation(item.Signal, request.Signal) &&
                            item.Signal.IOType == request.Signal.IOType &&
                            item.Signal.Usage == request.Signal.Usage)
                        .ToArray();
                    if (sameLocation.Length == 1)
                    {
                        missingAliases.Add(new MissingSignalAlias(request, sameLocation[0]));
                        continue;
                    }
                }

                missing.Add(request);
                continue;
            }

            var match = result.Candidates[0];
            bindings.Add(new ExistingSignalBinding(request, match.Signal, match.Parent));
        }

        return new SignalResolutionPlan(bindings, missing, missingAliases, issues);
    }

    private static MatchResult FindMatches(
        FeeInterfaceSignal requested,
        IReadOnlyCollection<(FeeInterface Parent, FeeInterfaceSignal Signal)> available)
    {
        var byTag = string.IsNullOrWhiteSpace(requested.Tag)
            ? []
            : available.Where(item => string.Equals(
                    item.Signal.Tag,
                    requested.Tag,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (byTag.Length == 1)
        {
            if (HasLocation(requested) && !SameLocation(byTag[0].Signal, requested))
                return new MatchResult(byTag, "EXISTING_SIGNAL_IDENTITY_CONFLICT");
            return new MatchResult(byTag, null);
        }

        var candidates = byTag.Length > 0
            ? byTag
            : available.Where(item => SameLocation(item.Signal, requested)).ToArray();
        if (candidates.Length <= 1)
            return new MatchResult(candidates, null);

        var byLocation = HasLocation(requested)
            ? candidates.Where(item => SameLocation(item.Signal, requested)).ToArray()
            : [];
        if (byLocation.Length == 1)
            return new MatchResult(byLocation, null);
        if (byLocation.Length > 1)
            candidates = byLocation;

        var byTypeAndUsage = candidates.Where(item =>
                item.Signal.IOType == requested.IOType && item.Signal.Usage == requested.Usage)
            .ToArray();
        if (byTypeAndUsage.Length == 1)
            return new MatchResult(byTypeAndUsage, null);
        if (byTypeAndUsage.Length > 1)
            candidates = byTypeAndUsage;

        return new MatchResult(candidates, "EXISTING_SIGNAL_AMBIGUOUS");
    }

    private static string BuildConflictMessage(
        SignalResolutionRequest request,
        IReadOnlyCollection<(FeeInterface Parent, FeeInterfaceSignal Signal)> candidates)
    {
        var locations = string.Join(
            "; ",
            candidates.Take(5).Select(item =>
                $"{item.Parent.Name}: {SignalIdentity(item.Signal)} [{SignalLocation(item.Signal)}]"));
        return $"Signal '{SignalIdentity(request.Signal)}' aus Container '{request.ContainerName}' " +
               $"konnte nicht eindeutig und widerspruchsfrei aufgelöst werden. Treffer: {locations}.";
    }

    private static bool HasLocation(FeeInterfaceSignal signal) =>
        !string.IsNullOrWhiteSpace(signal.Address) || !string.IsNullOrWhiteSpace(signal.Path);

    private static bool SameLocation(FeeInterfaceSignal left, FeeInterfaceSignal right)
    {
        var hasAddress = !string.IsNullOrWhiteSpace(right.Address);
        var hasPath = !string.IsNullOrWhiteSpace(right.Path);
        return (hasAddress || hasPath) &&
               (!hasAddress || string.Equals(left.Address, right.Address, StringComparison.OrdinalIgnoreCase)) &&
               (!hasPath || string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
    }

    private static string SignalIdentity(FeeInterfaceSignal signal) =>
        !string.IsNullOrWhiteSpace(signal.Tag)
            ? signal.Tag
            : SignalLocation(signal);

    private static string SignalLocation(FeeInterfaceSignal signal) =>
        !string.IsNullOrWhiteSpace(signal.Address)
            ? signal.Address
            : !string.IsNullOrWhiteSpace(signal.Path)
                ? signal.Path
                : "ohne Adresse/Pfad";

    private sealed record MatchResult(
        (FeeInterface Parent, FeeInterfaceSignal Signal)[] Candidates,
        string? Conflict);
}

public sealed record GrobGenerationInterfaceResolution(
    FeeInterface? Interface,
    VisualIssue? Issue)
{
    public bool IsValid => Interface is not null && Issue is null;
}

/// <summary>Strictly identifies the installed Grob generation interface.</summary>
public static class GrobGenerationInterfaceResolver
{
    public const string InterfaceName = "Grob Generation Interface";
    public const string ProviderName = "GrobGenerationInterface.Interface.GrobInterfaceProvider";

    public static GrobGenerationInterfaceResolution Resolve(IEnumerable<FeeInterface> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        var source = interfaces.Where(item => item is not null).ToArray();
        var strict = source.Where(IsStrictMatch).ToArray();
        if (strict.Length == 1)
            return new GrobGenerationInterfaceResolution(strict[0], null);

        if (strict.Length > 1)
        {
            return Failure(
                "GROB_GENERATION_INTERFACE_AMBIGUOUS",
                $"{strict.Length} Interfaces erfüllen Name, Provider-GUID und Provider des Grob Generation Interface. " +
                "Die Generierung wurde vor dem ersten Schreibzugriff abgebrochen.");
        }

        var partial = source.Where(item =>
                Same(item.Name, InterfaceName) ||
                item.ProviderGuid == Defines.GrobGenerationInterfaceProviderGuid ||
                Same(item.ProviderName, ProviderName))
            .ToArray();
        if (partial.Length > 0)
        {
            return Failure(
                "GROB_GENERATION_INTERFACE_INCONSISTENT",
                "Ein möglicher Treffer für das Grob Generation Interface besitzt widersprüchliche " +
                "Name-/Providerdaten. Erwartet werden Name 'Grob Generation Interface', Provider-GUID " +
                $"'{Defines.GrobGenerationInterfaceProviderGuid:D}' und Provider '{ProviderName}'.");
        }

        return Failure(
            "GROB_GENERATION_INTERFACE_MISSING",
            "Das Grob Generation Interface wurde nicht gefunden. Plugin installieren/aktivieren und " +
            "FEE-Objekte erneut aktualisieren; es wurde noch nichts erzeugt.");
    }

    private static bool IsStrictMatch(FeeInterface item) =>
        Same(item.Name, InterfaceName) &&
        item.ProviderGuid == Defines.GrobGenerationInterfaceProviderGuid &&
        Same(item.ProviderName, ProviderName);

    private static bool Same(string? left, string right) =>
        string.Equals(left?.Trim(), right, StringComparison.OrdinalIgnoreCase);

    private static GrobGenerationInterfaceResolution Failure(string code, string message) =>
        new(null, new VisualIssue(VisualIssueSeverity.Error, code, message));
}
