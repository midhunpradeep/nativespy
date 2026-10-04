using System.Text.Json;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Host;

public interface IAgentOperationHandler
{
    string OperationName { get; }

    Task<AgentHandlerResult> HandleAsync(
        AgentRequestContext context,
        JsonElement payload,
        CancellationToken cancellationToken);
}

public sealed class AgentHandlerResult
{
    private AgentHandlerResult(JsonElement? payload, OperationErrorDto? operationError)
    {
        Payload = payload;
        OperationError = operationError;
    }

    public JsonElement? Payload { get; }

    public OperationErrorDto? OperationError { get; }

    public static AgentHandlerResult Success(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("A successful handler result requires a non-null payload.", nameof(payload));
        }

        return new AgentHandlerResult(payload.Clone(), null);
    }

    public static AgentHandlerResult Error(OperationErrorDto error)
    {
        return new AgentHandlerResult(null, error ?? throw new ArgumentNullException(nameof(error)));
    }
}
