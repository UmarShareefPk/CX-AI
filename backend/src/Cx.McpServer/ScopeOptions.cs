using Cx.Core.Domain;
using Cx.Core.Security;

namespace Cx.McpServer;

/// <summary>
/// The identity this server process acts as. It is supplied by the API through the process environment when it launches the
/// server, and cannot be changed by anything the model says.
/// </summary>
public sealed class ScopeOptions
{
    public const string Section = "Cx:Scope";

    public UserRole? Role { get; set; }
    public string? DealerId { get; set; }

    public DataScope ToDataScope() => Role == UserRole.Admin ? DataScope.Admin : DataScope.ForDealer(DealerId!.Trim());
}
