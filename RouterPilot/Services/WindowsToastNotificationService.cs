using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Notifications;

namespace RouterPilot.Services;

public sealed class WindowsToastNotificationService : IToastNotificationService
{
    public const string AppUserModelId = "TCDemo777.RouterPilot";

    public Task<ToastDeliveryResult> SendAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(ToastDeliveryResult.PlatformUnsupported);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ToastDeliveryResult>(cancellationToken);
        }

        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
            return Task.FromResult(ToastDeliveryResult.Delivered);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine("Windows toast registration unavailable: " + ex.GetType().Name);
            return Task.FromResult(ToastDeliveryResult.RegistrationUnavailable);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Windows toast dispatch failed: " + ex.GetType().Name);
            return Task.FromResult(ToastDeliveryResult.DispatchFailed);
        }
    }
}
