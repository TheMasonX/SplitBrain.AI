using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public interface IInferenceNodeFactory
{
    IInferenceNode Create(NodeConfiguration config);
}
