using MongoDB.Bson.Serialization.Attributes;

namespace Cx.Core.Domain;

/// <summary>Survey question 1: "How would you recommend Honda to your friends and family?"</summary>
public enum RecommendAnswer { HighlyRecommend, Recommend, MightRecommend, NotRecommend }

/// <summary>Survey question 2: "Did you have any missing part or defect in your new car?"</summary>
public enum VehicleConditionAnswer { AllGood, PartMissing, Defect, PartMissingAndDefect }

public enum UserRole { Admin, Dealer }

public sealed class Dealer
{
    /// <summary>Dealer code, e.g. HND-1001. Used as the primary key so it is readable in tool output.</summary>
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Region { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class Customer
{
    [BsonId] public string Id { get; set; } = "";
    public string DealerId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string VehicleModel { get; set; } = "";
    public int VehicleYear { get; set; }
    public string Vin { get; set; } = "";
    public DateTime PurchaseDate { get; set; }

    [BsonIgnore] public string FullName => $"{FirstName} {LastName}";
}

public sealed class SurveyResponse
{
    [BsonId] public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string DealerId { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public RecommendAnswer Recommend { get; set; }
    public VehicleConditionAnswer VehicleCondition { get; set; }

    /// <summary>Stored (not recomputed on read) so aggregations stay a plain $sum/$avg.</summary>
    public int RecommendScore { get; set; }
    public int ConditionScore { get; set; }
    public double NetScore { get; set; }
}

public sealed class AppUser
{
    [BsonId] public string Id { get; set; } = "";
    public string Username { get; set; } = "";

    /// <summary>DEMO ONLY: plain text by explicit request. Replace with a salted hash (e.g. PasswordHasher) before real use.</summary>
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }

    /// <summary>Null for admins; the only dealer a dealer user may see.</summary>
    public string? DealerId { get; set; }
}

/// <summary>One retrievable piece of policy text plus its embedding, persisted so restarts never re-embed.</summary>
public sealed class PolicyChunk
{
    [BsonId] public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string ContentHash { get; set; } = "";

    /// <summary>provider/model that produced <see cref="Embedding"/>. Vectors from different models are never mixed.</summary>
    public string EmbeddingModel { get; set; } = "";
    public float[] Embedding { get; set; } = [];
    public int Dimensions { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class Conversation
{
    [BsonId] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Each entry is one serialized Microsoft.Extensions.AI ChatMessage (incl. tool calls and reasoning).</summary>
    public List<string> Messages { get; set; } = [];
}
