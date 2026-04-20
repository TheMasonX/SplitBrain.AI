using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public interface IMcpResponse
{
    Meta Meta { get; init; }
    McpError? Error { get; init; }
}
