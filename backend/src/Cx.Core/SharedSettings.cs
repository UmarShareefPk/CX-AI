using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace Cx.Core;

public static class SharedSettings
{
    public const string FileName = "cx.settings.json";

    /// <summary>
    /// Adds backend/cx.settings.json (Mongo, AI and MCP settings shared by the API, seeder and MCP server) as the LOWEST
    /// priority source, so appsettings, user-secrets, environment variables and the command line all override it.
    /// </summary>
    public static IConfigurationBuilder AddCxSharedSettings(this IConfigurationBuilder builder)
    {
        var source = new JsonConfigurationSource
        {
            Path = FileName,
            Optional = false,
            FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory),
        };
        source.ResolveFileProvider();
        builder.Sources.Insert(0, source);
        return builder;
    }
}
