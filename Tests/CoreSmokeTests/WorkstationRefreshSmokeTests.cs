using System.Reflection;
using VIBN_Tools.Application.VM;
using VIBN_Tools.Core.ViCo;
using VIBN_Tools.Infrastructure.ViCo;

internal static class WorkstationRefreshSmokeTests
{
    internal static async Task VerifyAsync(string directory)
    {
        var network = new Network(); var sessions = new Sessions();
        var workstation = new ViCoWorkstation("PC1", "PC1", "", "", "", "", [], []);
        var settings = new Settings();
        using var vm = new ViCoSearchPageVM(new SnapshotCatalog(workstation), new ViCoWorkstationSearch(),
            _ => Task.FromResult<IViCoRelatedPathResolver>(null!), network, new NullRemoteDesktopService(), sessions,
            new NullExternalPathLauncher(), new DisabledOnlineRefreshService(), new NullWorkstationConfigurationService(),
            settings, new JsonViCoLastActiveSnapshotStore(Path.Combine(directory, "refresh-snapshot.json")),
            new ViCoWorkspaceContext(), _ => { });
        await vm.InitializeAsync();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (sessions.Calls == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
        if (sessions.Calls == 0) throw new InvalidOperationException("Initial session lookup did not execute.");
        var pings = network.Calls; var sessionReads = sessions.Calls;
        var method = typeof(ViCoSearchPageVM).GetMethod("RefreshAllDataAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(vm, null)!;
        if (network.Calls <= pings || sessions.Calls <= sessionReads)
            throw new InvalidOperationException("Manual refresh reused a ping or RDP-session cache entry.");
        if (!vm.DataUpdatedAtText.StartsWith("Datenstand: ", StringComparison.Ordinal) || vm.DataUpdatedAtText.Contains("noch nicht"))
            throw new InvalidOperationException("Displayed refresh timestamp was not updated.");
        Console.WriteLine("Manual workstation refresh bypassed ping and RDP caches and displayed its data timestamp.");
    }
    private sealed class Network : INetworkAvailabilityService
    {
        public int Calls;
        public Task<bool> PingAsync(string hostName, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Calls); return Task.FromResult(true); }
    }
    private sealed class Sessions : IRemoteSessionService
    {
        public int Calls;
        public Task<ViCoRemoteSessionInfo> GetSessionInfoAsync(string hostName, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Calls); return Task.FromResult(ViCoRemoteSessionInfo.NotAvailable); }
    }
    private sealed class Settings : IViCoAutoRefreshSettingsStore
    {
        public Task<ViCoAutoRefreshSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(ViCoAutoRefreshSettings.Default);
        public Task SaveAsync(ViCoAutoRefreshSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
