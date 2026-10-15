using Microsoft.Extensions.Logging.Abstractions;
using OrderAccumulator.Application;
using OrderAccumulator.Domain;
using OrderAccumulator.Infrastructure.Fix;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;

namespace OrderAccumulator.Tests;

public class AcceptorApplicationTests
{
    private static readonly SessionID Session = new("FIX.4.4", "ACCUMULATOR", "GENERATOR");

    private static (AcceptorApplication App, OrderAcceptanceService Service, OrderJournal Journal) Create()
    {
        var service = new OrderAcceptanceService();
        var journal = new OrderJournal();
        var app = new AcceptorApplication(new FixSessionMonitor(), service, journal,
            NullLogger<AcceptorApplication>.Instance);
        return (app, service, journal);
    }

    private static NewOrderSingle Order(string clOrdId, string symbol, char side, decimal qty, decimal price)
    {
        var message = new NewOrderSingle(
            new ClOrdID(clOrdId), new QuickFix.Fields.Symbol(symbol), new Side(side),
            new TransactTime(DateTime.UtcNow), new OrdType(OrdType.LIMIT));
        message.Set(new OrderQty(qty));
        message.Set(new Price(price));
        return message;
    }

    [Fact]
    public void Accepted_order_returns_ExecType_New_and_updates_exposure()
    {
        var (app, service, _) = Create();

        var report = app.Process(Order("A1", "PETR4", Side.BUY, 100, 10.50m), Session);

        Assert.Equal(ExecType.NEW, report.ExecType.Value);
        Assert.Equal(OrdStatus.NEW, report.OrdStatus.Value);
        Assert.Equal("A1", report.ClOrdID.Value);
        Assert.Equal("PETR4", report.Symbol.Value);
        Assert.Equal(100m, report.LeavesQty.Value);
        Assert.Equal(1_050m, service.GetExposure(Domain.Symbol.PETR4));
    }

    [Fact]
    public void Order_over_limit_returns_Rejected_with_reason_and_keeps_exposure()
    {
        var (app, service, _) = Create();
        app.Process(Order("A1", "PETR4", Side.BUY, 99_999, 999.99m), Session);
        var before = service.GetExposure(Domain.Symbol.PETR4);

        var report = app.Process(Order("A2", "PETR4", Side.BUY, 1_000, 10m), Session);

        Assert.Equal(ExecType.REJECTED, report.ExecType.Value);
        Assert.Equal(OrdStatus.REJECTED, report.OrdStatus.Value);
        Assert.Equal(OrdRejReason.ORDER_EXCEEDS_LIMIT, report.OrdRejReason.Value);
        Assert.Equal(0m, report.LeavesQty.Value);
        Assert.Equal(before, service.GetExposure(Domain.Symbol.PETR4));
    }

    [Theory]
    [InlineData("XXXX1", Side.BUY, 10, 1.00)]   // símbolo desconhecido
    [InlineData("PETR4", Side.BUY, 10.5, 1.00)] // quantidade fracionária
    [InlineData("PETR4", '9', 10, 1.00)]        // lado não suportado
    public void Unmappable_order_is_rejected_without_touching_exposure(string symbol, char side, decimal qty, decimal price)
    {
        var (app, service, _) = Create();

        var report = app.Process(Order("B1", symbol, side, qty, price), Session);

        Assert.Equal(ExecType.REJECTED, report.ExecType.Value);
        Assert.All(service.GetExposures().Values, v => Assert.Equal(0m, v));
    }

    [Fact]
    public void Duplicate_ClOrdId_is_answered_again_but_counted_once()
    {
        var (app, service, journal) = Create();

        var first = app.Process(Order("D1", "VALE3", Side.BUY, 10, 5m), Session);
        var second = app.Process(Order("D1", "VALE3", Side.BUY, 10, 5m), Session);

        Assert.Equal(ExecType.NEW, first.ExecType.Value);
        Assert.Equal(ExecType.NEW, second.ExecType.Value);
        Assert.Equal(50m, service.GetExposure(Domain.Symbol.VALE3));

        var (recent, totals, _) = journal.Snapshot(10);
        Assert.Equal(1, totals.Total);
        Assert.Equal(1, totals.Duplicates);
        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public void Every_order_is_recorded_in_the_journal()
    {
        var (app, _, journal) = Create();

        app.Process(Order("J1", "PETR4", Side.SELL, 10, 2m), Session);

        var (recent, _, _) = journal.Snapshot(1);
        var entry = Assert.Single(recent);
        Assert.Equal("J1", entry.ClOrdId);
        Assert.Equal("Sell", entry.Side);
        Assert.Equal("GENERATOR", entry.Counterparty);
        Assert.Equal(20m, entry.Notional);
        Assert.Equal(-20m, entry.ExposureAfter);
    }
}
