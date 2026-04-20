using Orchestrator.Core.Enums;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public interface IRoutingService
{
    Task<InferenceResult> RouteAsync(TaskType taskType, InferenceRequest request, CancellationToken cancellationToken = default);
}
