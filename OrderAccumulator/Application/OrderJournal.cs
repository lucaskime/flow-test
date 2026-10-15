namespace OrderAccumulator.Application;

/// <summary>Registro observável de cada ordem processada, usado pelo painel em tempo real e para auditoria.</summary>
public sealed record OrderJournalEntry(
    DateTimeOffset Timestamp,
    string Counterparty,
    string ClOrdId,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal Price,
    decimal Notional,
    string Status,
    string? RejectReason,
    string? Text,
    bool Duplicate,
    decimal ExposureAfter);

public sealed record JournalTotals(int Total, int Accepted, int Rejected, int Duplicates);

public sealed class OrderJournal
{
    private const int MaxEntries = 10_000;

    private readonly object _gate = new();
    private readonly List<OrderJournalEntry> _entries = [];
    private int _dropped;

    /// <summary>Disparado a cada ordem registrada.</summary>
    public event Action? Changed;

    /// <summary>Repõe o histórico persistido na inicialização, sem disparar eventos.</summary>
    public void Seed(IEnumerable<OrderJournalEntry> entries)
    {
        lock (_gate)
        {
            foreach (var entry in entries)
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                {
                    _entries.RemoveAt(0);
                    _dropped++;
                }
            }
        }
    }

    public void Record(OrderJournalEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
                _dropped++;
            }
        }

        Changed?.Invoke();
    }

    public (IReadOnlyList<OrderJournalEntry> Recent, JournalTotals Totals,
            IReadOnlyDictionary<string, decimal>? Audit) Snapshot(int recent)
    {
        lock (_gate)
        {
            var fresh = _entries.Where(e => !e.Duplicate).ToList();
            var totals = new JournalTotals(
                fresh.Count,
                fresh.Count(e => e.Status == "Accepted"),
                fresh.Count(e => e.Status == "Rejected"),
                _entries.Count(e => e.Duplicate));

            // Recalcula a exposição só com as ordens aceitas; indisponível se o diário foi truncado.
            IReadOnlyDictionary<string, decimal>? audit = _dropped > 0
                ? null
                : fresh.Where(e => e.Status == "Accepted")
                       .GroupBy(e => e.Symbol)
                       .ToDictionary(g => g.Key, g => g.Sum(e => e.Side == "Buy" ? e.Notional : -e.Notional));

            return (_entries.TakeLast(recent).Reverse().ToList(), totals, audit);
        }
    }
}
