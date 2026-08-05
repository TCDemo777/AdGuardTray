using RouterPilot.Models;

namespace RouterPilot.Services;

public sealed record BlockedServiceMutationResult(
    IReadOnlyList<BlockedServiceItem> Services,
    AdGuardBlockedServicesConfig Config);

public sealed class BlockedServiceMutationService
{
    private readonly IRouterManagerProvider _provider;
    private readonly IAdGuardServiceCatalogueProvider _catalogueProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BlockedServiceMutationService(IRouterManagerProvider provider, IAdGuardServiceCatalogueProvider catalogueProvider)
    {
        _provider = provider;
        _catalogueProvider = catalogueProvider;
    }

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
            AdGuardBlockedServicesConfig currentConfig = await router.GetBlockedServicesConfigAsync();
            var current = currentConfig.EnabledIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            current.UnionWith(desired.Except(original, StringComparer.OrdinalIgnoreCase));
            current.ExceptWith(original.Except(desired, StringComparer.OrdinalIgnoreCase));
            await router.UpdateBlockedServicesAsync(current, currentConfig.ScheduleJson);
            AdGuardBlockedServicesConfig confirmed = await router.GetBlockedServicesConfigAsync();
            await _catalogueProvider.RefreshAsync(router, token);
            return new(_catalogueProvider.Services, confirmed);
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
            AdGuardBlockedServicesConfig config = await router.GetBlockedServicesConfigAsync();
            var ids = config.EnabledIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string id in serviceIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (action == AdGuardServiceScheduleAction.Block) ids.Add(id); else ids.Remove(id);
            }
            await router.UpdateBlockedServicesAsync(ids, config.ScheduleJson);
            config = await router.GetBlockedServicesConfigAsync();
            await _catalogueProvider.RefreshAsync(router, token);
            return new(_catalogueProvider.Services, config);
        }
        finally { _gate.Release(); }
    }

}
