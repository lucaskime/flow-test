using Microsoft.Extensions.Logging.Abstractions;
using OrderAccumulator.Application;
using OrderAccumulator.Domain;
using OrderAccumulator.Infrastructure;
using OrderAccumulator.Infrastructure.Fix;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;
using Symbol = OrderAccumulator.Domain.Symbol;

namespace OrderAccumulator.Tests;

public sealed class PersistenceAndDuplicateTests : IDisposable
{
    private const string Cp = "GENERATOR";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flowa-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "decisions.jsonl");

    private FileDecisionStore NewStore() => new(FilePath, NullLogger<FileDecisionStore>.Instance);

    private static OrderRequest Order(string id, OrderSide side, int qty, decimal price, Symbol symbol = Symbol.PETR4) =>
        new(id, symbol, side, qty, price);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Exposure_and_deduplication_survive_restart()
    {
        using (var store = NewStore())
        {
            var first = new OrderAcceptanceService(store);
            first.Evaluate(Cp, Order("1", OrderSide.Buy, 99_999, 999.99m));
            first.Evaluate(Cp, Order("2", OrderSide.Sell, 10, 5m, Symbol.VALE3));
            Assert.False(first.Evaluate(Cp, Order("3", OrderSide.Buy, 1_000, 10m)).Accepted);
        }

        using var reopened = NewStore();
        var restarted = new OrderAcceptanceService(reopened);

        Assert.Equal(99_998_000.01m, restarted.GetExposure(Symbol.PETR4));
        Assert.Equal(-50m, restarted.GetExposure(Symbol.VALE3));

        var replay = restarted.Evaluate(Cp, Order("1", OrderSide.Buy, 99_999, 999.99m));
        Assert.True(replay.IsDuplicate);
        Assert.True(replay.Accepted);
        Assert.Equal(99_998_000.01m, restarted.GetExposure(Symbol.PETR4));

        // O limite continua valendo sobre o estado reconstruído.
        Assert.False(restarted.Evaluate(Cp, Order("4", OrderSide.Buy, 1_000, 10m)).Accepted);
    }

    [Fact]
    public void Truncated_last_line_is_discarded()
    {
        using (var store = NewStore())
        {
            new OrderAcceptanceService(store).Evaluate(Cp, Order("1", OrderSide.Buy, 10, 10m));
        }

        File.AppendAllText(FilePath, "{\"timestamp\":\"2026-01-01T00:00:00+00:00\",\"counterp");

        using var reopened = NewStore();
        Assert.Equal(100m, new OrderAcceptanceService(reopened).GetExposure(Symbol.PETR4));
    }

    [Fact]
    public void Same_ClOrdId_with_different_content_is_rejected_as_duplicate_and_not_counted()
    {
        var sut = new OrderAcceptanceService();
        sut.Evaluate(Cp, Order("1", OrderSide.Buy, 100, 10m));

        var decision = sut.Evaluate(Cp, Order("1", OrderSide.Buy, 500, 10m));

        Assert.False(decision.Accepted);
        Assert.Equal(OrderRejectReason.DuplicateOrder, decision.RejectReason);
        Assert.True(decision.IsDuplicate);
        Assert.Equal(1_000m, sut.GetExposure(Symbol.PETR4));
    }

    [Fact]
    public void Store_failure_does_not_change_exposure()
    {
        var sut = new OrderAcceptanceService(new FailingStore());

        Assert.Throws<IOException>(() => sut.Evaluate(Cp, Order("1", OrderSide.Buy, 10, 10m)));
        Assert.Equal(0m, sut.GetExposure(Symbol.PETR4));
    }

    [Fact]
    public void Missing_field_produces_BusinessMessageReject_with_reference()
    {
        var message = new NewOrderSingle(
            new ClOrdID("M1"), new QuickFix.Fields.Symbol("PETR4"), new Side(Side.BUY),
            new TransactTime(DateTime.UtcNow), new OrdType(OrdType.LIMIT));
        message.Set(new OrderQty(10)); // sem Price

        var app = new AcceptorApplication(new FixSessionMonitor(), new OrderAcceptanceService(), new OrderJournal(),
            NullLogger<AcceptorApplication>.Instance);
        var error = Assert.ThrowsAny<Exception>(() => app.Process(message, new SessionID("FIX.4.4", "A", "G")));

        var reject = AcceptorApplication.BuildBusinessReject(message, error);

        Assert.Equal("M1", reject.BusinessRejectRefID.Value);
        Assert.Equal(BusinessRejectReason.CONDITIONALLY_REQUIRED_FIELD_MISSING, reject.BusinessRejectReason.Value);
    }

    private sealed class FailingStore : IDecisionStore
    {
        public void Append(StoredDecision decision) => throw new IOException("disk full");

        public IReadOnlyList<StoredDecision> LoadAll() => [];
    }
}
