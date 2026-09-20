namespace Cx.Core.Security;

public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>
/// Who is asking. Every read goes through this so tenant isolation lives in one place:
/// a dealer scope can only ever resolve to its own dealer id, an admin scope may see everything.
/// </summary>
public sealed record DataScope(string? DealerId)
{
    public static DataScope Admin { get; } = new((string?)null);
    public static DataScope ForDealer(string dealerId) => new(dealerId);

    public bool IsAdmin => DealerId is null;

    /// <summary>
    /// Returns the dealer filter to apply. Dealers get their own id (asking for another dealer is an error);
    /// admins get whatever they asked for (null = all dealers).
    /// </summary>
    public string? ResolveDealer(string? requestedDealerId)
    {
        if (IsAdmin) return string.IsNullOrWhiteSpace(requestedDealerId) ? null : requestedDealerId.Trim();

        if (!string.IsNullOrWhiteSpace(requestedDealerId)
            && !string.Equals(requestedDealerId.Trim(), DealerId, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("You can only access data for your own dealership.");

        return DealerId;
    }

    public void RequireAdmin()
    {
        if (!IsAdmin) throw new ForbiddenException("This information is only available to administrators.");
    }
}
