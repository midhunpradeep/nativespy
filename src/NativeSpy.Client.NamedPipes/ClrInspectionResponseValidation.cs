using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.NamedPipes;

internal sealed class ClrResponseSemanticException : Exception
{
    public ClrResponseSemanticException(string message)
        : base(message)
    {
    }
}

internal static class ClrInspectionResponseValidation
{
    public static void ValidateDescribe(
        DescribeObjectRequestDto request,
        DescribeObjectResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (!SameManagedObjectIdentity(request.Object, response.Object))
        {
            Invalid("The described object does not match the requested managed object.");
        }

        if (request.Object.TypeIdentity is not null
            && response.Object.TypeIdentity is not null
            && !SameTypeIdentityKey(request.Object.TypeIdentity, response.Object.TypeIdentity))
        {
            Invalid("The described object returned an incompatible type identity.");
        }
    }

    public static void ValidateListMembers(
        ListMembersRequestDto request,
        ListMembersResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (response.Members.Count > request.PageSize)
        {
            Invalid("The member response contains more entries than the requested page size.");
        }

        for (var index = 0; index < response.Members.Count; index++)
        {
            var descriptor = response.Members[index];
            if (!MatchesFilter(request.Filter, descriptor.Kind))
            {
                Invalid($"The member response entry at index {index} does not match the requested filter.");
            }

            ValidateMemberScope(request.Object, descriptor.Member, index);
            for (var previous = 0; previous < index; previous++)
            {
                if (SameMemberIdentity(descriptor.Member, response.Members[previous].Member))
                {
                    Invalid($"The member response contains duplicate identity at index {index}.");
                }
            }
        }
    }

    public static void ValidateReadFieldValues(
        ReadFieldValuesRequestDto request,
        ReadFieldValuesResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (response.Results.Count != request.Members.Count)
        {
            Invalid("The field response count does not match the requested member count.");
        }

        for (var index = 0; index < response.Results.Count; index++)
        {
            var result = response.Results[index];
            if (!IsMemberInScope(request.Object, result.Member)
                && !(result.Outcome == ClrReadOutcome.Unavailable
                    && result.ErrorCode == OperationErrorCode.InvalidMemberReference))
            {
                Invalid($"The field response entry at index {index} is outside the requested managed-object scope.");
            }

            if (!SameMemberIdentity(request.Members[index], result.Member))
            {
                Invalid($"The field response member at index {index} does not match the request.");
            }

            ValidateMemberReadResult(result, allowInvalidMemberReference: true, index);
        }
    }

    public static void ValidateReadPropertyValue(
        ReadPropertyValueRequestDto request,
        ReadPropertyValueResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var result = response.Result;
        ValidateMemberScope(request.Object, result.Member, 0);
        if (!SameMemberIdentity(request.Member, result.Member))
        {
            Invalid("The property response member does not match the request.");
        }

        // A property reference is resolved before invoking the getter.  An
        // invalid reference therefore remains a top-level operation failure;
        // it is not a legitimate member-local success outcome.
        ValidateMemberReadResult(result, allowInvalidMemberReference: false, 0);
    }

    private static void ValidateMemberReadResult(
        MemberReadResultDto result,
        bool allowInvalidMemberReference,
        int index)
    {
        switch (result.Outcome)
        {
            case ClrReadOutcome.Available:
                if (result.Value is null || result.ErrorCode is not null || result.TargetException is not null)
                {
                    Invalid($"The member result at index {index} has an invalid available branch.");
                }

                break;
            case ClrReadOutcome.Unsupported:
                if (result.Value is not null || result.ErrorCode is not null || result.TargetException is not null)
                {
                    Invalid($"The member result at index {index} has an invalid unsupported branch.");
                }

                break;
            case ClrReadOutcome.Unavailable:
                if (result.Value is not null
                    || result.TargetException is not null
                    || result.ErrorCode is not OperationErrorCode memberError
                    || !ClrInspectionContractLimits.IsMemberLocalUnavailableError(memberError)
                    || (!allowInvalidMemberReference
                        && memberError == OperationErrorCode.InvalidMemberReference))
                {
                    Invalid($"The member result at index {index} has an invalid unavailable branch.");
                }

                break;
            case ClrReadOutcome.TargetFailed:
                if (result.Value is not null || result.ErrorCode is not null || result.TargetException is null)
                {
                    Invalid($"The member result at index {index} has an invalid target-failed branch.");
                }

                break;
            default:
                Invalid($"The member result at index {index} has an unknown outcome.");
                break;
        }
    }

    private static void ValidateMemberScope(
        ManagedObjectRefDto @object,
        MemberRefDto member,
        int index)
    {
        if (!IsMemberInScope(@object, member))
        {
            Invalid($"The member response entry at index {index} is outside the requested managed-object scope.");
        }
    }

    private static bool IsMemberInScope(ManagedObjectRefDto @object, MemberRefDto member)
    {
        return string.Equals(member.SessionId, @object.Handle.SessionId, StringComparison.Ordinal)
            && string.Equals(member.BoundaryId, @object.Handle.BoundaryId, StringComparison.Ordinal);
    }

    private static bool SameManagedObjectIdentity(
        ManagedObjectRefDto expected,
        ManagedObjectRefDto actual)
    {
        return SameHandle(expected.Handle, actual.Handle)
            && string.Equals(expected.BoundaryId, actual.BoundaryId, StringComparison.Ordinal);
    }

    private static bool SameHandle(HandleRefDto expected, HandleRefDto actual)
    {
        return string.Equals(expected.SessionId, actual.SessionId, StringComparison.Ordinal)
            && string.Equals(expected.HandleId, actual.HandleId, StringComparison.Ordinal)
            && expected.Generation == actual.Generation
            && expected.Kind == actual.Kind
            && string.Equals(expected.BoundaryId, actual.BoundaryId, StringComparison.Ordinal);
    }

    private static bool SameTypeIdentityKey(TypeIdentityDto expected, TypeIdentityDto actual)
    {
        return string.Equals(expected.TypeId, actual.TypeId, StringComparison.Ordinal)
            && string.Equals(expected.BoundaryId, actual.BoundaryId, StringComparison.Ordinal);
    }

    private static bool SameMemberIdentity(MemberRefDto expected, MemberRefDto actual)
    {
        return string.Equals(expected.SessionId, actual.SessionId, StringComparison.Ordinal)
            && string.Equals(expected.MemberId, actual.MemberId, StringComparison.Ordinal)
            && string.Equals(expected.BoundaryId, actual.BoundaryId, StringComparison.Ordinal)
            && string.Equals(expected.DeclaringTypeId, actual.DeclaringTypeId, StringComparison.Ordinal);
    }

    private static bool MatchesFilter(ClrMemberKindFilter filter, ClrMemberKind kind)
    {
        return filter switch
        {
            ClrMemberKindFilter.All => true,
            ClrMemberKindFilter.Fields => kind == ClrMemberKind.Field,
            ClrMemberKindFilter.Properties => kind == ClrMemberKind.Property,
            _ => false
        };
    }

    private static void Invalid(string message)
    {
        throw new ClrResponseSemanticException(message);
    }
}
