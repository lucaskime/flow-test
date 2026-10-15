namespace OrderAccumulator.Application;

/// <summary>Decisão já tomada, na forma persistida: base para reconstruir exposição e deduplicação.</summary>
public sealed record StoredDecision(
    DateTimeOffset Timestamp,
    string Counterparty,
    OrderRequest Order,
    OrderDecision Decision);

/// <summary>
/// Persistência append-only das decisões. A exposição é derivada: reaplicar as decisões aceitas, em ordem,
/// reconstrói o estado. Trocar o backend (banco, event store) não exige mudar o serviço.
/// </summary>
public interface IDecisionStore
{
    /// <summary>Deve ser durável ao retornar; se lançar, a ordem não é confirmada.</summary>
    void Append(StoredDecision decision);

    IReadOnlyList<StoredDecision> LoadAll();
}

/// <summary>Sem persistência (testes e execução efêmera).</summary>
public sealed class NullDecisionStore : IDecisionStore
{
    public void Append(StoredDecision decision) { }

    public IReadOnlyList<StoredDecision> LoadAll() => [];
}
