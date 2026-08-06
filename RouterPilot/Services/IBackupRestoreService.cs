using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RouterPilot.Models;

namespace RouterPilot.Services;

public interface IBackupRestoreService
{
    string BackupFolder { get; }

    Task<BackupOperationResult> CreateBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<BackupInspection> InspectAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<BackupOperationResult> RestoreAsync(
        BackupInspection inspection,
        IReadOnlyCollection<string> selectedFiles,
        CancellationToken cancellationToken = default);
}
