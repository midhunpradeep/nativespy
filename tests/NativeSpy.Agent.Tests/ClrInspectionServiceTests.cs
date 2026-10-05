using NativeSpy.Agent;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;

namespace NativeSpy.Agent.Tests;

public sealed class ClrInspectionServiceTests
{
    [Fact]
    public void Listing_members_does_not_execute_property_getters()
    {
        using var session = new ClrAgentSession();
        using var service = new ClrInspectionService(session);
        var target = new InspectionTarget();
        var reference = AssertRegistration(session.Register(target));

        var result = service.ListMembers(
            new ListMembersRequestDto(reference, 128, ClrMemberKindFilter.All));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!.Members, member => member.Name == nameof(InspectionTarget.CountingProperty));
        Assert.Equal(0, target.GetterReads);
    }

    [Fact]
    public void Field_reads_are_bounded_and_use_the_closed_value_union()
    {
        using var session = new ClrAgentSession();
        using var service = new ClrInspectionService(session);
        var target = new InspectionTarget();
        var reference = AssertRegistration(session.Register(target));
        var members = AssertSuccess(service.ListMembers(
            new ListMembersRequestDto(reference, 128, ClrMemberKindFilter.Fields))).Members;

        var result = AssertSuccess(service.ReadFieldValues(
            new ReadFieldValuesRequestDto(reference, members.Select(member => member.Member))));

        var longText = AssertAvailable(result, members, nameof(InspectionTarget.LongText)).Value!;
        Assert.Equal(ClrValueKind.String, longText.Kind);
        Assert.True(longText.StringTruncated);
        Assert.Equal(ClrInspectionLimits.MaxStringCodeUnits, longText.ReturnedStringCodeUnitLength);

        var enumValue = AssertAvailable(result, members, nameof(InspectionTarget.EnumValue)).Value!;
        Assert.Equal(ClrValueKind.Enum, enumValue.Kind);
        Assert.Equal("Ready", enumValue.EnumName);
        Assert.Equal("7", enumValue.EnumUnderlyingValue);

        var referenceValue = AssertAvailable(result, members, nameof(InspectionTarget.Child)).Value!;
        Assert.Equal(ClrValueKind.ObjectReference, referenceValue.Kind);
        Assert.NotNull(referenceValue.ObjectReference);

        var structValue = AssertAvailable(result, members, nameof(InspectionTarget.StructValue)).Value!;
        Assert.Equal(ClrValueKind.ValueType, structValue.Kind);
        Assert.Equal(3, structValue.StructFields!.Count);
    }

    [Fact]
    public void Scalar_conversion_preserves_wire_semantics_at_the_agent_boundary()
    {
        using var session = new ClrAgentSession();
        using var service = new ClrInspectionService(session);
        var target = new SemanticInspectionTarget();
        var reference = AssertRegistration(session.Register(target));
        var members = AssertSuccess(service.ListMembers(
            new ListMembersRequestDto(reference, 128, ClrMemberKindFilter.Fields))).Members;
        var result = AssertSuccess(service.ReadFieldValues(
            new ReadFieldValuesRequestDto(reference, members.Select(member => member.Member))));

        Assert.Equal("-128", AssertAvailable(result, members, nameof(SemanticInspectionTarget.SByteValue)).Value!.IntegerValue);
        Assert.Equal("18446744073709551615", AssertAvailable(result, members, nameof(SemanticInspectionTarget.UInt64Value)).Value!.IntegerValue);
        Assert.Equal("80000000", AssertAvailable(result, members, nameof(SemanticInspectionTarget.NegativeZeroSingle)).Value!.FloatingPointBits);
        Assert.Equal("8000000000000000", AssertAvailable(result, members, nameof(SemanticInspectionTarget.NegativeZeroDouble)).Value!.FloatingPointBits);
        Assert.Equal("FFC00000", AssertAvailable(result, members, nameof(SemanticInspectionTarget.NaNSingle)).Value!.FloatingPointBits);
        Assert.Equal(decimal.GetBits(target.DecimalValue), AssertAvailable(result, members, nameof(SemanticInspectionTarget.DecimalValue)).Value!.DecimalBits);

        var utc = AssertAvailable(result, members, nameof(SemanticInspectionTarget.UtcDateTime)).Value!;
        Assert.Equal(target.UtcDateTime.Ticks, utc.DateTimeTicks);
        Assert.Equal(DateTimeKind.Utc, utc.DateTimeKind);

        var offset = AssertAvailable(result, members, nameof(SemanticInspectionTarget.OffsetDateTime)).Value!;
        Assert.Equal(target.OffsetDateTime.Ticks, offset.DateTimeOffsetClockTicks);
        Assert.Equal((short)-90, offset.DateTimeOffsetOffsetMinutes);

        var duration = AssertAvailable(result, members, nameof(SemanticInspectionTarget.Duration)).Value!;
        Assert.Equal(target.Duration.Ticks, duration.TimeSpanTicks);

        var guid = AssertAvailable(result, members, nameof(SemanticInspectionTarget.GuidValue)).Value!;
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", guid.GuidValue);

        var byteEnum = AssertAvailable(result, members, nameof(SemanticInspectionTarget.ByteEnumValue)).Value!;
        Assert.Equal(ClrIntegerKind.Byte, byteEnum.EnumUnderlyingKind);
        Assert.Equal("255", byteEnum.EnumUnderlyingValue);
        var longEnum = AssertAvailable(result, members, nameof(SemanticInspectionTarget.LongEnumValue)).Value!;
        Assert.Equal(ClrIntegerKind.Int64, longEnum.EnumUnderlyingKind);
        Assert.Equal(long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture), longEnum.EnumUnderlyingValue);

        var boundaryString = AssertAvailable(result, members, nameof(SemanticInspectionTarget.SurrogateBoundaryText)).Value!;
        Assert.True(boundaryString.StringTruncated);
        Assert.Equal(4095, boundaryString.ReturnedStringCodeUnitLength);
        Assert.False(char.IsHighSurrogate(boundaryString.StringValue![^1]));

        var structValue = AssertAvailable(result, members, nameof(SemanticInspectionTarget.StructValue)).Value!;
        Assert.Equal(3, structValue.StructFields!.Count);
        var nestedValue = Assert.Single(structValue.StructFields, field => field.Name == nameof(InspectionStruct.Nested));
        Assert.Equal(ClrValueKind.ValueType, nestedValue.Value!.Kind);
        Assert.True(nestedValue.Value.StructNotExpanded);
        Assert.Empty(nestedValue.Value.StructFields!);
    }

    [Fact]
    public async Task Property_reads_are_explicit_and_detach_target_exception_identity()
    {
        using var session = new ClrAgentSession();
        using var service = new ClrInspectionService(session);
        var target = new InspectionTarget();
        var reference = AssertRegistration(session.Register(target));
        var members = AssertSuccess(service.ListMembers(
            new ListMembersRequestDto(reference, 128, ClrMemberKindFilter.Properties))).Members;
        var counting = Assert.Single(members, member => member.Name == nameof(InspectionTarget.CountingProperty));
        var throwing = Assert.Single(members, member => member.Name == nameof(InspectionTarget.ThrowingProperty));

        Assert.Equal(0, target.GetterReads);
        var value = await service.ReadPropertyValueAsync(
            new ReadPropertyValueRequestDto(reference, counting.Member),
            new DirectScheduler(),
            CancellationToken.None);
        Assert.True(value.IsSuccess);
        Assert.Equal(ClrReadOutcome.Available, value.Value!.Result.Outcome);
        Assert.Equal("getter", value.Value.Result.Value!.StringValue);
        Assert.Equal(1, target.GetterReads);

        var failure = await service.ReadPropertyValueAsync(
            new ReadPropertyValueRequestDto(reference, throwing.Member),
            new DirectScheduler(),
            CancellationToken.None);
        Assert.True(failure.IsSuccess);
        var targetFailure = failure.Value!.Result;
        Assert.Equal(ClrReadOutcome.TargetFailed, targetFailure.Outcome);
        Assert.Equal(typeof(InvalidOperationException).FullName, targetFailure.TargetException!.ExceptionType.FullName);
        Assert.True(targetFailure.TargetException.WasReflectionWrapper);
    }

    [Fact]
    public void Continuations_are_authenticated_and_member_ids_are_not_rebound()
    {
        using var session = new ClrAgentSession();
        using var service = new ClrInspectionService(session);
        var target = new InspectionTarget();
        var reference = AssertRegistration(session.Register(target));
        var first = AssertSuccess(service.ListMembers(
            new ListMembersRequestDto(reference, 1, ClrMemberKindFilter.All)));
        Assert.NotNull(first.NextContinuationToken);

        var second = AssertSuccess(service.ListMembers(
            new ListMembersRequestDto(reference, 1, ClrMemberKindFilter.All, first.NextContinuationToken)));
        Assert.NotEqual(first.Members[0].Member.MemberId, second.Members[0].Member.MemberId);

        var continuationParts = first.NextContinuationToken!.Split('.');
        var forgedSignatureFirstCharacter = continuationParts[1][0] == 'A' ? 'B' : 'A';
        var forged = continuationParts[0] + "." + forgedSignatureFirstCharacter + continuationParts[1][1..];
        var invalid = service.ListMembers(
            new ListMembersRequestDto(reference, 1, ClrMemberKindFilter.All, forged));
        Assert.False(invalid.IsSuccess);
        Assert.Equal(OperationErrorCode.InvalidContinuation, invalid.Error!.Code);

        var member = first.Members[0].Member;
        var wrong = new MemberRefDto("other-session", member.MemberId, member.BoundaryId, member.DeclaringTypeId);
        var fieldRead = service.ReadFieldValues(new ReadFieldValuesRequestDto(reference, new[] { wrong }));
        Assert.Equal(OperationErrorCode.InvalidMemberReference, fieldRead.Value!.Results[0].ErrorCode);
    }

    private static MemberReadResultDto AssertAvailable(
        ReadFieldValuesResponseDto response,
        IReadOnlyList<MemberDescriptorDto> members,
        string memberName)
    {
        var descriptor = Assert.Single(members, member => member.Name == memberName);
        var result = Assert.Single(response.Results, candidate =>
            candidate.Member.MemberId == descriptor.Member.MemberId);
        Assert.Equal(ClrReadOutcome.Available, result.Outcome);
        return result;
    }

    private static T AssertSuccess<T>(ClrInspectionResult<T> result)
        where T : class
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!;
    }

    private static ManagedObjectRefDto AssertRegistration(ManagedObjectRegistrationResult result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Reference!;
    }

    private sealed class DirectScheduler : IClrInvocationScheduler
    {
        public Task<ClrInvocationResult> InvokeAsync(
            object target,
            Func<object?> callback,
            CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(ClrInvocationResult.Success(callback()));
            }
            catch (Exception exception)
            {
                return Task.FromResult(ClrInvocationResult.Failed(exception));
            }
        }
    }

    private sealed class InspectionTarget
    {
        public string LongText = new('x', ClrInspectionLimits.MaxStringCodeUnits + 100);
        public InspectionChild Child = new();
        public InspectionStruct StructValue = new(4, "struct");
        public InspectionEnum EnumValue = InspectionEnum.Ready;
        public decimal DecimalValue = 5.25m;
        public int GetterReads;

        public string CountingProperty
        {
            get
            {
                GetterReads++;
                return "getter";
            }
        }

        public string ThrowingProperty => throw new InvalidOperationException("not transported");
    }

    private sealed class SemanticInspectionTarget
    {
        public sbyte SByteValue = sbyte.MinValue;
        public ulong UInt64Value = ulong.MaxValue;
        public float NegativeZeroSingle = -0f;
        public double NegativeZeroDouble = -0d;
        public float NaNSingle = float.NaN;
        public decimal DecimalValue = 5.25m;
        public DateTime UtcDateTime = new(638000000000000000, DateTimeKind.Utc);
        public DateTimeOffset OffsetDateTime = new(2024, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(-90));
        public TimeSpan Duration = TimeSpan.FromTicks(-123456789);
        public Guid GuidValue = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        public InspectionByteEnum ByteEnumValue = InspectionByteEnum.Max;
        public InspectionLongEnum LongEnumValue = InspectionLongEnum.Min;
        public string SurrogateBoundaryText = new string('x', ClrInspectionLimits.MaxStringCodeUnits - 1) + "\ud800tail";
        public InspectionStruct StructValue = new(4, "struct");
    }

    private enum InspectionByteEnum : byte
    {
        Max = byte.MaxValue
    }

    private enum InspectionLongEnum : long
    {
        Min = long.MinValue
    }

    private sealed class InspectionChild
    {
        public int Number = 2;
    }

    private enum InspectionEnum
    {
        Ready = 7
    }

    private readonly struct InspectionStruct
    {
        public InspectionStruct(int number, string text)
        {
            Number = number;
            Text = text;
            Nested = new InspectionNestedStruct(9);
        }

        public readonly int Number;
        public readonly string Text;
        public readonly InspectionNestedStruct Nested;
    }

    private readonly struct InspectionNestedStruct
    {
        public InspectionNestedStruct(int value)
        {
            Value = value;
        }

        public readonly int Value;
    }
}
