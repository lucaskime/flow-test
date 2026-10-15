using OrderGenerator.Domain;

namespace OrderGenerator.Application;

public sealed record OrderRequest(Symbol Symbol, OrderSide Side, int Quantity, decimal Price);

public enum OrderOutcomeStatus
{
    Accepted,
    Rejected,
    Timeout,
    SessionUnavailable
}

public sealed record OrderOutcome(string ClOrdId, OrderOutcomeStatus Status, string? Text = null);

/// <summary>Envia a ordem ao Accumulator e correlaciona o ExecutionReport por ClOrdID.</summary>
public interface IOrderSubmissionService
{
    Task<OrderOutcome> SubmitAsync(OrderRequest request, CancellationToken cancellationToken);
}
