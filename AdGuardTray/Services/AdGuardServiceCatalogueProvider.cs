using System.Collections.ObjectModel;
using System.Windows.Threading;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public interface IAdGuardServiceCatalogueProvider
{
    ReadOnlyObservableCollection<BlockedServiceItem> Services { get; }
    string? LastError { get; }
    event EventHandler? CatalogueChanged;
    Task<bool> RefreshAsync(RouterManager? router = null, CancellationToken cancellationToken = default);
}

public sealed class AdGuardServiceCatalogueProvider : IAdGuardServiceCatalogueProvider
{
    private readonly IRouterManagerProvider _routerProvider;
    private readonly Dispatcher _dispatcher;
    private readonly ObservableCollection<BlockedServiceItem> _services = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public AdGuardServiceCatalogueProvider(IRouterManagerProvider routerProvider, Dispatcher dispatcher)
    {
        _routerProvider = routerProvider;
        _dispatcher = dispatcher;
        Services = new ReadOnlyObservableCollection<BlockedServiceItem>(_services);
    }

    public ReadOnlyObservableCollection<BlockedServiceItem> Services { get; }
    public string? LastError { get; private set; }
    public event EventHandler? CatalogueChanged;

    public async Task<bool> RefreshAsync(RouterManager? router = null, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            router ??= await _routerProvider.GetRouterManagerAsync(cancellationToken).ConfigureAwait(false);
            List<BlockedServiceItem> catalogue = await router.GetBlockedServiceCatalogueAsync().ConfigureAwait(false);
            if (catalogue.Count == 0)
            {
                LastError = "AdGuard Home returned an empty blocked-service catalogue.";
                return false;
            }

            LastError = null;
            await _dispatcher.InvokeAsync(() =>
            {
                _services.Clear();
                foreach (BlockedServiceItem item in catalogue.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                    _services.Add(item);
                CatalogueChanged?.Invoke(this, EventArgs.Empty);
            });
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            LastError = "The AdGuard service catalogue could not be refreshed.";
            return false;
        }
        finally { _refreshGate.Release(); }
    }
}
