using Microsoft.Extensions.Logging.Abstractions;
using OrderGenerator.Application;
using OrderGenerator.Domain;
using OrderGenerator.Infrastructure.Fix;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;

namespace OrderGenerator.Tests;

public class FixClientTests
{
    private static readonly SessionID Session = new("FIX.4.4", "GENERATOR", "ACCUMULATOR");

    private static ExecutionReport Report(string clOrdId, char execType, string? text = null)
    {
        var report = new ExecutionReport(
            new OrderID("1"), new ExecID("1"), new ExecType(execType),
            new OrdStatus(execType == ExecType.NEW ? OrdStatus.NEW : OrdStatus.REJECTED),
            new QuickFix.Fields.Symbol("PETR4"), new Side(Side.BUY),
            new LeavesQty(0), new CumQty(0), new AvgPx(0));
        report.Set(new ClOrdID(clOrdId));
        if (text is not null)
        {
            report.Set(new Text(text));
        }

        return report;
    }

    private static (InitiatorApplication App, PendingOrders Pending) CreateApp()
    {
        var pending = new PendingOrders();
        return (new InitiatorApplication(new FixSessionMonitor(), pending, NullLogger<InitiatorApplication>.Instance), pending);
    }

    [Fact]
    public async Task ExecutionReport_New_completes_pending_order_as_Accepted()
    {
        var (app, pending) = CreateApp();
        var task = pending.Register("C1");

        app.OnMessage(Report("C1", ExecType.NEW), Session);

        var outcome = await task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(new OrderOutcome("C1", OrderOutcomeStatus.Accepted), outcome);
    }

    [Fact]
    public async Task ExecutionReport_Rejected_carries_the_text()
    {
        var (app, pending) = CreateApp();
        var task = pending.Register("C2");

        app.OnMessage(Report("C2", ExecType.REJECTED, "Exposure limit exceeded"), Session);

        var outcome = await task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(OrderOutcomeStatus.Rejected, outcome.Status);
        Assert.Equal("Exposure limit exceeded", outcome.Text);
    }

    [Fact]
    public void Unexpected_ExecType_does_not_complete_the_order()
    {
        var (app, pending) = CreateApp();
        var task = pending.Register("C3");

        app.OnMessage(Report("C3", ExecType.CANCELED), Session);

        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void Report_for_unknown_ClOrdId_is_ignored()
    {
        var (app, _) = CreateApp();

        app.OnMessage(Report("UNKNOWN", ExecType.NEW), Session);
    }

    [Fact]
    public async Task Submit_without_FIX_session_returns_SessionUnavailable()
    {
        var service = new FixOrderSubmissionService(
            new FixSessionMonitor(), new PendingOrders(), NullLogger<FixOrderSubmissionService>.Instance);

        var outcome = await service.SubmitAsync(
            new OrderRequest(Domain.Symbol.PETR4, OrderSide.Buy, 10, 1m), CancellationToken.None);

        Assert.Equal(OrderOutcomeStatus.SessionUnavailable, outcome.Status);
    }
}
