using QuickFix;
using QuickFix.Store;
using QuickFix.Transport;

namespace OrderGenerator.Infrastructure.Fix;

/// <summary>
/// Liga o SocketInitiator ao ciclo de vida do host. A reconexão é feita pelo QuickFIX/n (ReconnectInterval);
/// uma falha inicial de conexão não derruba a aplicação.
/// </summary>
internal sealed class FixInitiatorHostedService(
    InitiatorApplication application,
    FixSettingsLoader settingsLoader,
    ILoggerFactory loggerFactory,
    ILogger<FixInitiatorHostedService> logger) : IHostedService, IDisposable
{
    private SocketInitiator? _initiator;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = settingsLoader.Load();
        _initiator = new SocketInitiator(
            application,
            new FileStoreFactory(settings),
            settings,
            loggerFactory);

        _initiator.Start();
        logger.LogInformation("FIX initiator started with {SessionCount} session(s)", settings.GetSessions().Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _initiator?.Stop();
        logger.LogInformation("FIX initiator stopped");
        return Task.CompletedTask;
    }

    public void Dispose() => _initiator?.Dispose();
}
