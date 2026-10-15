using OrderAccumulator.Application;
using OrderAccumulator.Infrastructure;

namespace OrderAccumulator.Infrastructure.Fix;

public static class FixServiceCollectionExtensions
{
    public static IServiceCollection AddFixAcceptor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FixOptions>(configuration.GetSection(FixOptions.SectionName));
        services.AddSingleton<FixSessionMonitor>();
        services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.SectionName));
        services.AddSingleton<IDecisionStore, FileDecisionStore>();
        services.AddSingleton<IOrderAcceptanceService, OrderAcceptanceService>();
        services.AddSingleton(sp =>
        {
            // O diário é reposto a partir das mesmas decisões, para a auditoria continuar consistente após reinício.
            var journal = new OrderJournal();
            journal.Seed(sp.GetRequiredService<IDecisionStore>().LoadAll().Select(d => new OrderJournalEntry(
                d.Timestamp, d.Counterparty, d.Order.ClOrdId, d.Order.Symbol.ToString(), d.Order.Side.ToString(),
                d.Order.Quantity, d.Order.Price, d.Order.Quantity * d.Order.Price,
                d.Decision.Accepted ? "Accepted" : "Rejected", d.Decision.RejectReason?.ToString(),
                d.Decision.Text, false, 0m)));
            return journal;
        });
        services.AddSingleton<FixSettingsLoader>();
        services.AddSingleton<AcceptorApplication>();
        services.AddHostedService<FixAcceptorHostedService>();
        return services;
    }
}
