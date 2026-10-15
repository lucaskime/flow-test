using OrderAccumulator.Domain;

namespace OrderAccumulator.Application;

/// <summary>
/// Exposição por símbolo e decisões já tomadas, mantidas em memória e persistidas em <see cref="IDecisionStore"/>.
/// Um único lock torna "consultar, projetar, validar limite, persistir e confirmar" uma operação atômica
/// e a deduplicação por (contraparte, ClOrdID) evita dupla contabilização em reenvios.
/// Ao iniciar, o estado é reconstruído a partir do store.
/// </summary>
public sealed class OrderAcceptanceService : IOrderAcceptanceService
{
    public const decimal ExposureLimit = 100_000_000m;
    public const int MaxQuantityExclusive = 100_000;
    public const decimal MaxPriceExclusive = 1_000m;

    private readonly object _gate = new();
    private readonly IDecisionStore _store;
    private readonly Dictionary<Symbol, decimal> _exposure = [];
    private readonly Dictionary<(string Counterparty, string ClOrdId), (OrderRequest Order, OrderDecision Decision)> _decisions = [];

    public OrderAcceptanceService(IDecisionStore? store = null)
    {
        _store = store ?? new NullDecisionStore();

        // Reconstrói o estado reaplicando as decisões persistidas, na ordem original.
        foreach (var stored in _store.LoadAll())
        {
            _decisions[(stored.Counterparty, stored.Order.ClOrdId)] = (stored.Order, stored.Decision);
            if (stored.Decision.Accepted)
            {
                _exposure[stored.Order.Symbol] = _exposure.GetValueOrDefault(stored.Order.Symbol) + SignedNotional(stored.Order);
            }
        }
    }

    public OrderDecision Evaluate(string counterpartyId, OrderRequest order)
    {
        lock (_gate)
        {
            var key = (counterpartyId, order.ClOrdId);
            if (_decisions.TryGetValue(key, out var previous))
            {
                // Reenvio idêntico repete a decisão; mesmo ClOrdID com conteúdo diferente é rejeitado.
                var decision = previous.Order == order
                    ? previous.Decision
                    : new OrderDecision(false, OrderRejectReason.DuplicateOrder,
                        $"ClOrdID {order.ClOrdId} already used with different content");

                return decision with
                {
                    IsDuplicate = true,
                    ExposureAfter = _exposure.GetValueOrDefault(previous.Order.Symbol)
                };
            }

            var result = Decide(order);

            // Persiste antes de confirmar em memória: se falhar, nada muda e a ordem não é respondida como aceita.
            var persisted = result with { ExposureAfter = 0m };
            _store.Append(new StoredDecision(DateTimeOffset.UtcNow, counterpartyId, order, persisted));

            if (result.Accepted)
            {
                _exposure[order.Symbol] = _exposure.GetValueOrDefault(order.Symbol) + SignedNotional(order);
            }

            _decisions[key] = (order, persisted);
            return result with { ExposureAfter = _exposure.GetValueOrDefault(order.Symbol) };
        }
    }

    public decimal GetExposure(Symbol symbol)
    {
        lock (_gate)
        {
            return _exposure.GetValueOrDefault(symbol);
        }
    }

    public IReadOnlyDictionary<Symbol, decimal> GetExposures()
    {
        lock (_gate)
        {
            return Enum.GetValues<Symbol>().ToDictionary(s => s, s => _exposure.GetValueOrDefault(s));
        }
    }

    private static decimal SignedNotional(OrderRequest order) =>
        order.Side == OrderSide.Buy ? order.Price * order.Quantity : -(order.Price * order.Quantity);

    // Pura: decide sem alterar estado. Deve ser chamado sob _gate.
    private OrderDecision Decide(OrderRequest order)
    {
        if (order.Quantity is <= 0 or >= MaxQuantityExclusive
            || order.Price <= 0 || order.Price >= MaxPriceExclusive
            || order.Price % 0.01m != 0)
        {
            return new OrderDecision(false, OrderRejectReason.InvalidOrder, "Invalid quantity or price");
        }

        var projected = _exposure.GetValueOrDefault(order.Symbol) + SignedNotional(order);
        if (Math.Abs(projected) > ExposureLimit)
        {
            return new OrderDecision(false, OrderRejectReason.ExposureLimitExceeded,
                $"Exposure limit of {ExposureLimit:N0} exceeded for {order.Symbol}");
        }

        return new OrderDecision(true);
    }
}
