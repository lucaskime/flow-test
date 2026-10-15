using OrderAccumulator.Domain;

namespace OrderAccumulator.Application;

/// <summary>Ordem recebida, já traduzida do FIX. Não referencia tipos do QuickFIX/n.</summary>
public sealed record OrderRequest(string ClOrdId, Symbol Symbol, OrderSide Side, int Quantity, decimal Price);

public enum OrderRejectReason
{
    ExposureLimitExceeded,
    InvalidOrder,
    DuplicateOrder
}

/// <summary>Decisão de negócio; ordem rejeitada nunca altera a exposição.</summary>
public sealed record OrderDecision(
    bool Accepted,
    OrderRejectReason? RejectReason = null,
    string? Text = null,
    decimal ExposureAfter = 0m,
    bool IsDuplicate = false);

/// <summary>Valida limite, atualiza exposição de forma atômica e garante idempotência por (contraparte, ClOrdID).</summary>
public interface IOrderAcceptanceService
{
    OrderDecision Evaluate(string counterpartyId, OrderRequest order);

    decimal GetExposure(Symbol symbol);

    IReadOnlyDictionary<Symbol, decimal> GetExposures();
}
