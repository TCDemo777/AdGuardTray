using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed record BlockedServiceMutationResult(
    IReadOnlyList<BlockedServiceItem> Services,
    AdGuardBlockedServicesConfig Config);

public sealed class BlockedServiceMutationService
{
    private readonly IRouterManagerProvider _provider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BlockedServiceMutationService(IRouterManagerProvider provider) => _provider = provider;

    public async Task<BlockedServiceMutationResult?> TryApplyManualChangesAsync(
        IEnumerable<string> originalBlockedIds, IEnumerable<string> desiredBlockedIds,
        CancellationToken token = default)
    {
        if (!await _gate.WaitAsync(0, token)) return null;
        try
        {
            token.ThrowIfCancellationRequested();
            var original = originalBlockedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var desired = desiredBlockedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            RouterManager router = await _provider.GetRouterManagerAsync();
            (_, AdGuardBlockedServicesConfig currentConfig) = await router.GetBlockedServicesAsync();
            var current = currentConfig.EnabledIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            current.UnionWith(desired.Except(original, StringComparer.OrdinalIgnoreCase));
            current.ExceptWith(original.Except(desired, StringComparer.OrdinalIgnoreCase));
            await router.UpdateBlockedServicesAsync(current, currentConfig.ScheduleJson);
            (List<BlockedServiceItem> services, AdGuardBlockedServicesConfig confirmed) = await router.GetBlockedServicesAsync();
            return new(services, confirmed);
        }
        finally { _gate.Release(); }
    }

    public async Task<BlockedServiceMutationResult> ApplyAsync(
        IEnumerable<string> serviceIds, AdGuardServiceScheduleAction action, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            RouterManager router = await _provider.GetRouterManagerAsync();
            (List<BlockedServiceItem> services, AdGuardBlockedServicesConfig config) = await router.GetBlockedServicesAsync();
            var ids = config.EnabledIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string id in serviceIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (action == AdGuardServiceScheduleAction.Block) ids.Add(id); else ids.Remove(id);
            }
            await router.UpdateBlockedServicesAsync(ids, config.ScheduleJson);
            (services, config) = await router.GetBlockedServicesAsync();
            return new(services, config);
        }
        finally { _gate.Release(); }
    }

}
