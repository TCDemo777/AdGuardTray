using System.Threading;
using System.Threading.Tasks;

namespace AdGuardTray.Services;

public interface IRouterManagerProvider : IAsyncDisposable
{
    Task<RouterManager> GetRouterManagerAsync(
        CancellationToken cancellationToken = default);

    void Invalidate();
}
