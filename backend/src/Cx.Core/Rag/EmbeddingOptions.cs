using System.ComponentModel.DataAnnotations;

namespace Cx.Core.Rag;

public sealed class EmbeddingOptions
{
    public const string Section = "Ai:Embeddings";

    /// <summary>ollama | openai. (Anthropic has no embeddings API.)</summary>
    [Required] public string Provider { get; set; } = "ollama";
    [Required] public string Model { get; set; } = "embeddinggemma";

    /// <summary>Some embedding models are trained with task prefixes; {title}/{text} and {query} are substituted.</summary>
    [Required] public string DocumentTemplate { get; set; } = "{title}\n{text}";
    [Required] public string QueryTemplate { get; set; } = "{query}";

    [Range(1, 256)] public int BatchSize { get; set; } = 16;

    /// <summary>Stored next to every vector so vectors from different models are never mixed.</summary>
    public string ModelKey => $"{Provider.ToLowerInvariant()}/{Model}";
}
