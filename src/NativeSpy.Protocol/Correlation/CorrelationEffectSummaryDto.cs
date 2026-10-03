using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationEffectSummaryDto
{
    public CorrelationEffectSummaryDto(
        IEnumerable<EffectCategory> categories,
        FrameworkStateEffect frameworkState,
        ApplicationCallbackEffect applicationCallbacks,
        IEnumerable<CallbackDetailDto> callbackDetails,
        VisibleMutationEffect visibleMutation,
        IEnumerable<string> operations)
    {
        Categories = ContractValidation.CopyRequired(categories, nameof(categories));
        if (Categories.Distinct().Count() != Categories.Count)
        {
            throw new ArgumentException("Effect categories must be unique.", nameof(categories));
        }

        FrameworkState = frameworkState;
        ApplicationCallbacks = applicationCallbacks;
        CallbackDetails = ContractValidation.CopyRequired(callbackDetails, nameof(callbackDetails));
        Operations = ContractValidation.CopyRequired(operations, nameof(operations));
        foreach (var operation in Operations)
        {
            ContractValidation.RequiredIdentifier(operation, nameof(operations));
        }
    }

    public IReadOnlyList<EffectCategory> Categories { get; }

    public FrameworkStateEffect FrameworkState { get; }

    public ApplicationCallbackEffect ApplicationCallbacks { get; }

    public IReadOnlyList<CallbackDetailDto> CallbackDetails { get; }

    public VisibleMutationEffect VisibleMutation { get; }

    public IReadOnlyList<string> Operations { get; }
}
