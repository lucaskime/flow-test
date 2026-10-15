namespace OrderAccumulator.Infrastructure.Fix;

public sealed class FixOptions
{
    public const string SectionName = "Fix";

    /// <summary>Arquivo .cfg do QuickFIX/n, relativo ao diretório da aplicação. Pode variar por ambiente.</summary>
    public string ConfigFile { get; init; } = "Configuration/fix/acceptor.cfg";

    /// <summary>Raiz do store de sessões (sequências e mensagens), relativa ao content root. Deve ser persistente.</summary>
    public string DataPath { get; init; } = "fix-data";
}
