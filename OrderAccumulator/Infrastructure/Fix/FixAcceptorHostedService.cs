using Microsoft.Extensions.Options;
using QuickFix;
using QuickFix.Store;

namespace OrderAccumulator.Infrastructure.Fix;

/// <summary>Liga o ThreadedSocketAcceptor ao ciclo de vida do host. O QuickFIX/n gerencia suas próprias threads.</summary>
internal sealed class FixAcceptorHostedService(
    AcceptorApplication application,
    FixSettingsLoader settingsLoader,
    ILoggerFactory loggerFactory,
    ILogger<FixAcceptorHostedService> logger) : IHostedService, IDisposable
{
    private ThreadedSocketAcceptor? _acceptor;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = settingsLoader.Load();
        _acceptor = new ThreadedSocketAcceptor(
            application,
            new FileStoreFactory(settings),
            settings,
            loggerFactory);

        _acceptor.Start();
        logger.LogInformation("FIX acceptor started with {SessionCount} session(s)", settings.GetSessions().Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _acceptor?.Stop();
        logger.LogInformation("FIX acceptor stopped");
        return Task.CompletedTask;
    }

    public void Dispose() => _acceptor?.Dispose();
}
