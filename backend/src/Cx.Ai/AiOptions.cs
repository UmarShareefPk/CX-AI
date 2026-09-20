using System.ComponentModel.DataAnnotations;

namespace Cx.Ai;

public static class Providers
{
    public const string Ollama = "ollama";
    public const string OpenAI = "openai";
    public const string Anthropic = "anthropic";
    public const string Google = "google";
}

public sealed class ModelRef
{
    [Required] public string Provider { get; set; } = Providers.Ollama;
    [Required] public string Model { get; set; } = "gemma4:e4b";
}

public sealed class OllamaOptions
{
    [Required, Url] public string Endpoint { get; set; } = "http://localhost:11434";
}

/// <summary>Hosted provider. Enabled only when an API key is present (environment variable or user-secrets, never the repo).</summary>
public sealed class HostedProviderOptions
{
    public string? ApiKey { get; set; }

    /// <summary>Optional base URL: lets the OpenAI adapter talk to any OpenAI-compatible server (LM Studio, vLLM, OpenRouter...).</summary>
    public string? Endpoint { get; set; }
    public List<string> Models { get; set; } = [];

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class AiOptions
{
    public const string Section = "Ai";

    [Required] public ModelRef Default { get; set; } = new();

    /// <summary>Upper bound on model-call/tool-call rounds for a single question (guards runaway agent loops).</summary>
    [Range(1, 20)] public int MaxToolIterations { get; set; } = 8;
    [Range(0, 2)] public float Temperature { get; set; } = 0.2f;
    [Range(64, 32000)] public int MaxOutputTokens { get; set; } = 2048;
    [Range(10, 900)] public int RequestTimeoutSeconds { get; set; } = 240;

    /// <summary>How many past user turns (with their tool calls) are replayed to the model.</summary>
    [Range(1, 50)] public int MaxHistoryTurns { get; set; } = 8;

    [Required] public OllamaOptions Ollama { get; set; } = new();
    public HostedProviderOptions OpenAI { get; set; } = new();
    public HostedProviderOptions Anthropic { get; set; } = new();
    public HostedProviderOptions Google { get; set; } = new();
}

public sealed class McpOptions
{
    public const string Section = "Mcp";

    /// <summary>Executable used to launch the built MCP server DLL.</summary>
    [Required] public string Command { get; set; } = "dotnet";

    /// <summary>Path to Cx.McpServer.dll. Relative paths resolve against the application base directory.</summary>
    [Required] public string ServerDll { get; set; } = "mcp/Cx.McpServer.dll";

    /// <summary>Idle MCP server processes are shut down after this many minutes.</summary>
    [Range(1, 1440)] public int IdleMinutes { get; set; } = 15;
}
