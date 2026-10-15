using System.Collections.Concurrent;
using OrderGenerator.Application;

namespace OrderGenerator.Infrastructure.Fix;

/// <summary>Correlaciona ExecutionReports recebidos com as requisições em espera, por ClOrdID.</summary>
internal sealed class PendingOrders
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OrderOutcome>> _pending = new();

    public Task<OrderOutcome> Register(string clOrdId)
    {
        var tcs = new TaskCompletionSource<OrderOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[clOrdId] = tcs;
        return tcs.Task;
    }

    public void Complete(OrderOutcome outcome)
    {
        if (_pending.TryRemove(outcome.ClOrdId, out var tcs))
        {
            tcs.TrySetResult(outcome);
        }
    }

    public void Discard(string clOrdId) => _pending.TryRemove(clOrdId, out _);
}
