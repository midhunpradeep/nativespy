using System.Text.Json.Serialization;

namespace NativeSpy.Protocol.Json;

internal sealed class ClrObjectPayloadWire
{
    public ManagedObjectWire? Object { get; set; }
}

internal sealed class DescribeObjectResponseWire
{
    public ManagedObjectWire? Object { get; set; }
}

internal sealed class MemberRefWire
{
    public string SessionId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string BoundaryId { get; set; } = string.Empty;
    public string DeclaringTypeId { get; set; } = string.Empty;
}

internal sealed class MemberDescriptorWire
{
    public MemberRefWire? Member { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TypeRefWire? ValueType { get; set; }
    public string? DisplaySignature { get; set; }
}

internal sealed class ListMembersRequestWire
{
    public ManagedObjectWire? Object { get; set; }
    public int PageSize { get; set; }
    public string Filter { get; set; } = string.Empty;
    public string? ContinuationToken { get; set; }
}

internal sealed class ListMembersResponseWire
{
    public MemberDescriptorWire[]? Members { get; set; }
    public string? NextContinuationToken { get; set; }
}

internal sealed class ReadFieldValuesRequestWire
{
    public ManagedObjectWire? Object { get; set; }
    public MemberRefWire[]? Members { get; set; }
}

internal sealed class ReadFieldValuesResponseWire
{
    public MemberReadResultWire[]? Results { get; set; }
}

internal sealed class ReadPropertyValueRequestWire
{
    public ManagedObjectWire? Object { get; set; }
    public MemberRefWire? Member { get; set; }
}

internal sealed class ReadPropertyValueResponseWire
{
    public MemberReadResultWire? Result { get; set; }
}

internal sealed class MemberReadResultWire
{
    public MemberRefWire? Member { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public ClrValueWire? Value { get; set; }
    public string? ErrorCode { get; set; }
    public TargetExceptionWire? TargetException { get; set; }
}

internal sealed class TargetExceptionWire
{
    public TypeIdentityWire? ExceptionType { get; set; }
    public bool WasReflectionWrapper { get; set; }
}

internal sealed class StructFieldWire
{
    public string Name { get; set; } = string.Empty;
    public TypeRefWire? FieldType { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public ClrValueWire? Value { get; set; }
}

internal sealed class ClrValueWire
{
    public string Kind { get; set; } = string.Empty;
    public bool? BooleanValue { get; set; }
    public string? IntegerKind { get; set; }
    public string? IntegerValue { get; set; }
    public string? FloatingPointKind { get; set; }
    public string? FloatingPointBits { get; set; }
    public string? FloatingPointDisplay { get; set; }
    public int[]? DecimalBits { get; set; }
    public string? DecimalDisplay { get; set; }
    public ushort? CharCodeUnit { get; set; }
    public string? StringValue { get; set; }
    public int? OriginalStringCodeUnitLength { get; set; }
    public int? ReturnedStringCodeUnitLength { get; set; }
    public bool? StringTruncated { get; set; }
    public string? GuidValue { get; set; }
    public long? DateTimeTicks { get; set; }
    public string? DateTimeKind { get; set; }
    public long? DateTimeOffsetClockTicks { get; set; }
    public short? DateTimeOffsetOffsetMinutes { get; set; }
    public long? TimeSpanTicks { get; set; }
    public TypeIdentityWire? EnumType { get; set; }
    public string? EnumUnderlyingKind { get; set; }
    public string? EnumUnderlyingValue { get; set; }
    public string? EnumName { get; set; }
    public TypeIdentityWire? RepresentedType { get; set; }
    public ManagedObjectWire? TypeObjectReference { get; set; }
    public TypeIdentityWire? ObjectType { get; set; }
    public ManagedObjectWire? ObjectReference { get; set; }
    public TypeIdentityWire? ValueType { get; set; }
    public StructFieldWire[]? StructFields { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StructTruncated { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StructNotExpanded { get; set; }
}
