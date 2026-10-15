using OrderGenerator.Application;

namespace OrderGenerator.Tests;

public class OrderRequestValidatorTests
{
    [Theory]
    [InlineData(1, 0.01)]
    [InlineData(99_999, 999.99)]
    [InlineData(100, 10.50)]
    public void Valid_orders_have_no_errors(int quantity, decimal price) =>
        Assert.Empty(OrderRequestValidator.Validate(quantity, price));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]
    public void Invalid_quantity_is_reported(int quantity) =>
        Assert.Contains("quantity", OrderRequestValidator.Validate(quantity, 10m).Keys);

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(1000)]
    [InlineData(1000.01)]
    [InlineData(1.005)]
    [InlineData(10.001)]
    public void Invalid_price_is_reported(decimal price) =>
        Assert.Contains("price", OrderRequestValidator.Validate(10, price).Keys);

    [Fact]
    public void Both_fields_can_fail_together() =>
        Assert.Equal(2, OrderRequestValidator.Validate(0, 0).Count);
}
