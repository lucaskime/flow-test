using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QuickFix;

namespace OrderGenerator.Infrastructure.Fix;

/// <summary>Carrega o .cfg do QuickFIX/n resolvendo caminhos absolutos para dicionário e store.</summary>
internal sealed class FixSettingsLoader(IOptions<FixOptions> options, IHostEnvironment environment)
{
    public SessionSettings Load()
    {
        var opts = options.Value;
        var configPath = Path.Combine(AppContext.BaseDirectory, opts.ConfigFile);
        var dictionary = Path.Combine(AppContext.BaseDirectory, "Configuration", "fix", "FIX44.xml");
        var storePath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, opts.DataPath, "store"));
        Directory.CreateDirectory(storePath);

        var text = File.ReadAllText(configPath)
            .Replace("${DataDictionary}", dictionary, StringComparison.Ordinal)
            .Replace("${StorePath}", storePath, StringComparison.Ordinal);

        return new SessionSettings(new StringReader(text));
    }
}
