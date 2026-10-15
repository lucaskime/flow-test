using OrderGenerator.Application;
using OrderGenerator.Domain;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX44;

namespace OrderGenerator.Infrastructure.Fix;

internal sealed class FixOrderSubmissionService(
    FixSessionMonitor monitor,
    PendingOrders pending,
    ILogger<FixOrderSubmissionService> logger) : IOrderSubmissionService
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);

    public async Task<OrderOutcome> SubmitAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        var clOrdId = Guid.NewGuid().ToString("N");
        var session = monitor.FirstLoggedOn;
        if (session is null)
        {
            return new OrderOutcome(clOrdId, OrderOutcomeStatus.SessionUnavailable, "Sessão FIX não está conectada.");
        }

        var message = new NewOrderSingle(
            new ClOrdID(clOrdId),
            new QuickFix.Fields.Symbol(request.Symbol.ToString()),
            new Side(request.Side == OrderSide.Buy ? Side.BUY : Side.SELL),
            new TransactTime(DateTime.UtcNow),
            new OrdType(OrdType.LIMIT));
        message.Set(new OrderQty(request.Quantity));
        message.Set(new Price(request.Price));
        message.Set(new TimeInForce(TimeInForce.DAY));

        var response = pending.Register(clOrdId);
        try
        {
            if (!Session.SendToTarget(message, session))
            {
                return new OrderOutcome(clOrdId, OrderOutcomeStatus.SessionUnavailable, "Falha ao enviar a ordem.");
            }

            logger.LogInformation("Order {ClOrdId} sent {Symbol} {Side} {Quantity}@{Price}",
                clOrdId, request.Symbol, request.Side, request.Quantity, request.Price);

            return await response.WaitAsync(ResponseTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Order {ClOrdId} timed out waiting for ExecutionReport", clOrdId);
            return new OrderOutcome(clOrdId, OrderOutcomeStatus.Timeout, "Sem resposta do OrderAccumulator.");
        }
        finally
        {
            pending.Discard(clOrdId);
        }
    }
}
