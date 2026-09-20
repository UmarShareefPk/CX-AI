using System.Diagnostics;

namespace Cx.McpServer;

/// <summary>
/// The MCP server is started by the API as a child process over stdio, so it cannot be launched from the IDE with F5.
/// Set CX_MCP_DEBUG in the API's environment (child processes inherit it) to debug it instead:
///   CX_MCP_DEBUG=1     call Debugger.Launch(): the OS / Visual Studio offers to attach a debugger to the new process
///   CX_MCP_DEBUG=wait  print the process id to stderr and wait up to 2 minutes for you to attach (Debug > Attach to Process)
/// Compiled out of Release builds, so a production build can never stall waiting for a debugger.
/// </summary>
internal static class DebugSupport
{
    public const string Variable = "CX_MCP_DEBUG";
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);

    [Conditional("DEBUG")]
    public static void AttachIfRequested()
    {
        var mode = Environment.GetEnvironmentVariable(Variable)?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(mode) || mode is "0" or "false" or "off") return;

        // stderr only: stdout is the MCP protocol channel.
        if (mode == "wait")
        {
            Console.Error.WriteLine($"[cx-mcp] {Variable}=wait: PID {Environment.ProcessId} is waiting up to {MaxWait.TotalSeconds:0}s for a debugger to attach...");
            var waited = Stopwatch.StartNew();
            while (!Debugger.IsAttached && waited.Elapsed < MaxWait) Thread.Sleep(200);
            Console.Error.WriteLine(Debugger.IsAttached ? "[cx-mcp] debugger attached." : "[cx-mcp] no debugger attached; continuing.");
            return;
        }

        Console.Error.WriteLine($"[cx-mcp] {Variable}={mode}: launching debugger for PID {Environment.ProcessId}...");
        Debugger.Launch();
    }
}
