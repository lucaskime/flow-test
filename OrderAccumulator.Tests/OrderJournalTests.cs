using OrderAccumulator.Application;

namespace OrderAccumulator.Tests;

public class OrderJournalTests
{
    private static OrderJournalEntry Entry(string id, string status, string side = "Buy", decimal notional = 100m,
        bool duplicate = false, string symbol = "PETR4") =>
        new(DateTimeOffset.UtcNow, "GENERATOR", id, symbol, side, 1, notional, notional, status, null, null, duplicate, 0);

    [Fact]
    public void Totals_ignore_duplicates_in_order_counts()
    {
        var sut = new OrderJournal();
        sut.Record(Entry("1", "Accepted"));
        sut.Record(Entry("2", "Rejected"));
        sut.Record(Entry("1", "Accepted", duplicate: true));

        var (_, totals, _) = sut.Snapshot(10);

        Assert.Equal(new JournalTotals(2, 1, 1, 1), totals);
    }

    [Fact]
    public void Audit_recomputes_exposure_from_accepted_orders_only()
    {
        var sut = new OrderJournal();
        sut.Record(Entry("1", "Accepted", "Buy", 1_000m));
        sut.Record(Entry("2", "Accepted", "Sell", 400m));
        sut.Record(Entry("3", "Rejected", "Buy", 9_999m));
        sut.Record(Entry("1", "Accepted", "Buy", 1_000m, duplicate: true));

        var (_, _, audit) = sut.Snapshot(10);

        Assert.Equal(600m, audit!["PETR4"]);
    }

    [Fact]
    public void Snapshot_returns_most_recent_first_and_raises_Changed()
    {
        var sut = new OrderJournal();
        var changes = 0;
        sut.Changed += () => changes++;

        sut.Record(Entry("1", "Accepted"));
        sut.Record(Entry("2", "Accepted"));
        sut.Record(Entry("3", "Accepted"));

        var (recent, _, _) = sut.Snapshot(2);

        Assert.Equal(3, changes);
        Assert.Equal(["3", "2"], recent.Select(e => e.ClOrdId));
    }
}
