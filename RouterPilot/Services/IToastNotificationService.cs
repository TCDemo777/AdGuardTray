using System.Threading;
using System.Threading.Tasks;

namespace RouterPilot.Services;

public interface IToastNotificationService
{
    Task<ToastDeliveryResult> SendAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);
}

public enum ToastDeliveryResult
{
    Delivered,
    PlatformUnsupported,
    RegistrationUnavailable,
    DispatchFailed
}
