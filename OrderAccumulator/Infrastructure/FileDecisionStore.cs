using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OrderAccumulator.Application;

namespace OrderAccumulator.Infrastructure;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>Arquivo JSONL das decisões, relativo ao content root. Deve ser um volume persistente.</summary>
    public string DecisionsFile { get; init; } = "fix-data/decisions.jsonl";
}

/// <summary>
/// Log append-only em JSON Lines com flush para o disco a cada decisão. Uma linha final truncada
/// (crash durante a escrita) é descartada na leitura: a ordem correspondente nunca foi respondida.
/// </summary>
public sealed class FileDecisionStore : IDecisionStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly ILogger<FileDecisionStore> _logger;
    private FileStream? _stream;

    public FileDecisionStore(IOptions<PersistenceOptions> options, IHostEnvironment environment,
        ILogger<FileDecisionStore> logger)
        : this(Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.DecisionsFile)), logger)
    {
    }

    public FileDecisionStore(string path, ILogger<FileDecisionStore> logger)
    {
        _path = path;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public IReadOnlyList<StoredDecision> LoadAll()
    {
        lock (_gate)
        {
            var result = new List<StoredDecision>();
            if (!File.Exists(_path))
            {
                return result;
            }

            var lines = File.ReadAllLines(_path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                try
                {
                    result.Add(JsonSerializer.Deserialize<StoredDecision>(lines[i], Json)!);
                }
                catch (JsonException) when (i == lines.Length - 1)
                {
                    _logger.LogWarning("Discarding truncated last line of {Path}", _path);
                }
            }

            return result;
        }
    }

    public void Append(StoredDecision decision)
    {
        var line = JsonSerializer.SerializeToUtf8Bytes(decision, Json);
        lock (_gate)
        {
            _stream ??= new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _stream.Write(line);
            _stream.WriteByte((byte)'\n');
            _stream.Flush(flushToDisk: true);
        }
    }

    public void Dispose() => _stream?.Dispose();
}
