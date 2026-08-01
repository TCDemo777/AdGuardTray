using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class IntelligenceService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private readonly HistoryRepository _historyRepository;
    private readonly DeviceHistoryService _deviceHistoryService;
    private readonly IReadOnlyList<BehaviourRule> _rules;
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private BehaviourAnalysis? _cachedAnalysis;
    private IReadOnlyList<BehaviourObservation> _cachedObservations =
        Array.Empty<BehaviourObservation>();

    public IntelligenceService(
        HistoryRepository historyRepository,
        DeviceHistoryService deviceHistoryService,
        IEnumerable<BehaviourRule> rules)
    {
        _historyRepository = historyRepository;
        _deviceHistoryService = deviceHistoryService;
        _rules = rules.ToArray();
    }

    public async Task<IReadOnlyList<BehaviourObservation>> AnalyzeAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        await _analysisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!force && _cachedAnalysis is not null &&
                now - _cachedAnalysis.GeneratedAtUtc < CacheDuration)
                return _cachedObservations;

            DateTimeOffset from = now.AddDays(-30);
            Task<IReadOnlyList<DeviceConnectionEvent>> deviceEventsTask =
                _historyRepository.GetEventsBetweenAsync(from, now, cancellationToken);
            Task<IReadOnlyList<WanMinuteSnapshot>> wanTask =
                _historyRepository.GetWanHistoryAsync(from, now, cancellationToken);
            Task<IReadOnlyList<RouterHealthMinuteSnapshot>> healthTask =
                _historyRepository.GetRouterHealthHistoryAsync(from, now, cancellationToken);
            await Task.WhenAll(deviceEventsTask, wanTask, healthTask).ConfigureAwait(false);

            var analysis = new BehaviourAnalysis
            {
                GeneratedAtUtc = now,
                Devices = _deviceHistoryService.Records.ToArray(),
                DeviceEvents = await deviceEventsTask.ConfigureAwait(false),
                WanHistory = await wanTask.ConfigureAwait(false),
                RouterHealth = await healthTask.ConfigureAwait(false)
            };
            var observations = new List<BehaviourObservation>();
            foreach (BehaviourRule rule in _rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                observations.AddRange(await rule.EvaluateAsync(analysis, cancellationToken)
                    .ConfigureAwait(false));
            }

            _cachedAnalysis = analysis;
            _cachedObservations = observations
                .OrderByDescending(item => item.Priority)
                .ThenByDescending(item => item.Severity)
                .ThenByDescending(item => item.Timestamp)
                .ToArray();
            return _cachedObservations;
        }
        finally
        {
            _analysisGate.Release();
        }
    }

    public async Task<DeviceBehaviourProfile?> GetDeviceProfileAsync(
        string macAddress,
        CancellationToken cancellationToken = default)
    {
        await AnalyzeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        string normalized = DeviceHistoryService.NormalizeMacAddress(macAddress);
        return _cachedAnalysis?.DeviceProfiles.GetValueOrDefault(normalized);
    }
}
