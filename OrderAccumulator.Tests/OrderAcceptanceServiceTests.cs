using OrderAccumulator.Application;
using OrderAccumulator.Domain;

namespace OrderAccumulator.Tests;

public class OrderAcceptanceServiceTests
{
    private const string Cp = "GENERATOR";

    private static OrderRequest Order(string id, OrderSide side, int qty, decimal price, Symbol symbol = Symbol.PETR4) =>
        new(id, symbol, side, qty, price);

    [Fact]
    public void Accepts_order_within_limit_and_updates_exposure()
    {
        var sut = new OrderAcceptanceService();

        var decision = sut.Evaluate(Cp, Order("1", OrderSide.Buy, 100, 10.50m));

        Assert.True(decision.Accepted);
        Assert.Equal(1050m, sut.GetExposure(Symbol.PETR4));
    }

    [Fact]
    public void Sell_reduces_exposure_and_limit_is_absolute()
    {
        var sut = new OrderAcceptanceService();

        Assert.True(sut.Evaluate(Cp, Order("1", OrderSide.Sell, 99_999, 999.99m)).Accepted);
        Assert.Equal(-99_998_000.01m, sut.GetExposure(Symbol.PETR4));

        var rejected = sut.Evaluate(Cp, Order("2", OrderSide.Sell, 1_000, 10m));
        Assert.False(rejected.Accepted);
        Assert.Equal(OrderRejectReason.ExposureLimitExceeded, rejected.RejectReason);
        Assert.Equal(-99_998_000.01m, sut.GetExposure(Symbol.PETR4));
    }

    [Fact]
    public void Rejected_order_does_not_change_exposure_and_symbols_are_independent()
    {
        var sut = new OrderAcceptanceService();
        sut.Evaluate(Cp, Order("1", OrderSide.Buy, 99_999, 999.99m));

        Assert.False(sut.Evaluate(Cp, Order("2", OrderSide.Buy, 1_000, 10m)).Accepted);
        Assert.True(sut.Evaluate(Cp, Order("3", OrderSide.Buy, 1_000, 10m, Symbol.VALE3)).Accepted);
        Assert.Equal(99_998_000.01m, sut.GetExposure(Symbol.PETR4));
        Assert.Equal(10_000m, sut.GetExposure(Symbol.VALE3));
    }

    [Fact]
    public void Duplicate_ClOrdId_is_not_counted_twice()
    {
        var sut = new OrderAcceptanceService();

        var first = sut.Evaluate(Cp, Order("1", OrderSide.Buy, 100, 10m));
        var second = sut.Evaluate(Cp, Order("1", OrderSide.Buy, 100, 10m));

        Assert.Equal(first.Accepted, second.Accepted);
        Assert.True(second.IsDuplicate);
        Assert.Equal(1_000m, sut.GetExposure(Symbol.PETR4));
    }

    [Theory]
    [InlineData(0, 10.00)]
    [InlineData(100_000, 10.00)]
    [InlineData(10, 0)]
    [InlineData(10, 1000)]
    [InlineData(10, 1.005)]
    public void Rejects_invalid_quantity_or_price(int qty, decimal price)
    {
        var sut = new OrderAcceptanceService();

        var decision = sut.Evaluate(Cp, Order("1", OrderSide.Buy, qty, price));

        Assert.False(decision.Accepted);
        Assert.Equal(OrderRejectReason.InvalidOrder, decision.RejectReason);
        Assert.Equal(0m, sut.GetExposure(Symbol.PETR4));
    }

    [Fact]
    public async Task Concurrent_orders_never_exceed_limit()
    {
        var sut = new OrderAcceptanceService();
        // 200 ordens de 999.990 cada: no máximo 100 cabem no limite de 100M.
        var tasks = Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => sut.Evaluate(Cp, Order(i.ToString(), OrderSide.Buy, 1_000, 999.99m))));

        var results = await Task.WhenAll(tasks);

        Assert.Equal(100, results.Count(r => r.Accepted));
        Assert.True(sut.GetExposure(Symbol.PETR4) <= OrderAcceptanceService.ExposureLimit);
    }
}
