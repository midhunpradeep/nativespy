using System.Text.Json;
using NativeSpy.Agent;
using NativeSpy.Protocol.Json;

namespace NativeSpy.Agent.Host;

public static class ClrInspectionHandlerFactory
{
    public static IReadOnlyList<IAgentOperationHandler> Create(
        ClrInspectionService inspection,
        IClrExecutionContextResolver executionContextResolver)
    {
        if (inspection is null)
        {
            throw new ArgumentNullException(nameof(inspection));
        }

        if (executionContextResolver is null)
        {
            throw new ArgumentNullException(nameof(executionContextResolver));
        }

        return new IAgentOperationHandler[]
        {
            new DescribeObjectHandler(inspection),
            new ListMembersHandler(inspection),
            new ReadFieldValuesHandler(inspection),
            new ReadPropertyValueHandler(inspection, executionContextResolver)
        };
    }
}

internal sealed class DescribeObjectHandler : IAgentOperationHandler
{
    private readonly ClrInspectionService _inspection;

    public DescribeObjectHandler(ClrInspectionService inspection)
    {
        _inspection = inspection;
    }

    public string OperationName => NativeSpy.Protocol.Common.ProtocolOperationNames.DescribeObject;

    public Task<AgentHandlerResult> HandleAsync(
        AgentRequestContext context,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var result = _inspection.DescribeObject(
            ClrInspectionJsonCodec.ReadDescribeObjectPayload(payload));
        return Task.FromResult(result.IsSuccess
            ? AgentHandlerResult.Success(ClrInspectionJsonCodec.SerializeDescribeObjectResponse(result.Value!))
            : AgentHandlerResult.Error(result.Error!));
    }
}

internal sealed class ListMembersHandler : IAgentOperationHandler
{
    private readonly ClrInspectionService _inspection;

    public ListMembersHandler(ClrInspectionService inspection)
    {
        _inspection = inspection;
    }

    public string OperationName => NativeSpy.Protocol.Common.ProtocolOperationNames.ListMembers;

    public Task<AgentHandlerResult> HandleAsync(
        AgentRequestContext context,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var result = _inspection.ListMembers(
            ClrInspectionJsonCodec.ReadListMembersPayload(payload));
        return Task.FromResult(result.IsSuccess
            ? AgentHandlerResult.Success(ClrInspectionJsonCodec.SerializeListMembersResponse(result.Value!))
            : AgentHandlerResult.Error(result.Error!));
    }
}

internal sealed class ReadFieldValuesHandler : IAgentOperationHandler
{
    private readonly ClrInspectionService _inspection;

    public ReadFieldValuesHandler(ClrInspectionService inspection)
    {
        _inspection = inspection;
    }

    public string OperationName => NativeSpy.Protocol.Common.ProtocolOperationNames.ReadFieldValues;

    public Task<AgentHandlerResult> HandleAsync(
        AgentRequestContext context,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var result = _inspection.ReadFieldValues(
            ClrInspectionJsonCodec.ReadReadFieldValuesPayload(payload));
        return Task.FromResult(result.IsSuccess
            ? AgentHandlerResult.Success(ClrInspectionJsonCodec.SerializeReadFieldValuesResponse(result.Value!))
            : AgentHandlerResult.Error(result.Error!));
    }
}

internal sealed class ReadPropertyValueHandler : IAgentOperationHandler
{
    private readonly ClrInspectionService _inspection;
    private readonly IClrExecutionContextResolver _executionContextResolver;

    public ReadPropertyValueHandler(
        ClrInspectionService inspection,
        IClrExecutionContextResolver executionContextResolver)
    {
        _inspection = inspection;
        _executionContextResolver = executionContextResolver;
    }

    public string OperationName => NativeSpy.Protocol.Common.ProtocolOperationNames.ReadPropertyValue;

    public async Task<AgentHandlerResult> HandleAsync(
        AgentRequestContext context,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var result = await _inspection.ReadPropertyValueAsync(
                ClrInspectionJsonCodec.ReadReadPropertyValuePayload(payload),
                new ClrInvocationScheduler(_executionContextResolver, context),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? AgentHandlerResult.Success(ClrInspectionJsonCodec.SerializeReadPropertyValueResponse(result.Value!))
            : AgentHandlerResult.Error(result.Error!);
    }
}
