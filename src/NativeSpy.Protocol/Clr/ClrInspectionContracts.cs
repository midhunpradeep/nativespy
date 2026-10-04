using System.Globalization;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Protocol.Clr;

public enum ClrMemberKind
{
    Field,
    Property
}

public enum ClrMemberKindFilter
{
    All,
    Fields,
    Properties
}

public enum ClrReadOutcome
{
    Available,
    Unsupported,
    Unavailable,
    TargetFailed
}

public enum ClrIntegerKind
{
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64
}

public enum ClrFloatingPointKind
{
    Single,
    Double
}

public enum ClrValueKind
{
    Null,
    Boolean,
    Integer,
    FloatingPoint,
    Decimal,
    Char,
    String,
    Guid,
    DateTime,
    DateTimeOffset,
    TimeSpan,
    Enum,
    TypeObject,
    ValueType,
    ObjectReference
}

public enum ClrStructFieldOutcome
{
    Available,
    Unsupported,
    Unavailable
}

public static class ClrInspectionContractLimits
{
    public const int MaxMembersPerPage = 128;
    public const int MaxFieldsPerBatch = 64;
    public const int MaxStringCodeUnits = 4096;
    public const int MaxStructFields = 32;
    public const int MaxContinuationTokenBytes = 512;
}

public sealed class MemberRefDto
{
    public MemberRefDto(
        string sessionId,
        string memberId,
        string boundaryId,
        string declaringTypeId)
    {
        SessionId = ContractValidation.RequiredIdentifier(sessionId, nameof(sessionId));
        MemberId = ContractValidation.RequiredIdentifier(memberId, nameof(memberId));
        BoundaryId = ContractValidation.RequiredIdentifier(boundaryId, nameof(boundaryId));
        DeclaringTypeId = ContractValidation.RequiredIdentifier(declaringTypeId, nameof(declaringTypeId));
    }

    public string SessionId { get; }

    public string MemberId { get; }

    public string BoundaryId { get; }

    public string DeclaringTypeId { get; }
}

public sealed class MemberDescriptorDto
{
    public MemberDescriptorDto(
        MemberRefDto member,
        ClrMemberKind kind,
        string name,
        TypeRefDto valueType,
        string? displaySignature = null)
    {
        Member = member ?? throw new ArgumentNullException(nameof(member));
        Kind = ContractValidation.RequireDefinedEnum(kind, nameof(kind));
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        DisplaySignature = ContractValidation.OptionalText(displaySignature, nameof(displaySignature));
    }

    public MemberRefDto Member { get; }

    public ClrMemberKind Kind { get; }

    public string Name { get; }

    public TypeRefDto ValueType { get; }

    public string? DisplaySignature { get; }
}

public sealed class DescribeObjectRequestDto
{
    public DescribeObjectRequestDto(ManagedObjectRefDto @object)
    {
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
    }

    public ManagedObjectRefDto Object { get; }
}

public sealed class DescribeObjectResponseDto
{
    public DescribeObjectResponseDto(ManagedObjectRefDto @object)
    {
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
        if (@object.TypeIdentity is null)
        {
            throw new ArgumentException("The described object must include a type identity.", nameof(@object));
        }
    }

    public ManagedObjectRefDto Object { get; }
}

public sealed class ListMembersRequestDto
{
    public ListMembersRequestDto(
        ManagedObjectRefDto @object,
        int pageSize,
        ClrMemberKindFilter filter = ClrMemberKindFilter.All,
        string? continuationToken = null)
    {
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "The page size must be positive.");
        }

        PageSize = pageSize;
        Filter = ContractValidation.RequireDefinedEnum(filter, nameof(filter));
        ContinuationToken = ContractValidation.OptionalText(continuationToken, nameof(continuationToken));
    }

    public ManagedObjectRefDto Object { get; }

    public int PageSize { get; }

    public ClrMemberKindFilter Filter { get; }

    public string? ContinuationToken { get; }
}

public sealed class ListMembersResponseDto
{
    public ListMembersResponseDto(
        IEnumerable<MemberDescriptorDto> members,
        string? nextContinuationToken = null)
    {
        Members = ContractValidation.CopyRequired(members, nameof(members));
        if (Members.Count > ClrInspectionContractLimits.MaxMembersPerPage)
        {
            throw new ArgumentException("A member response exceeds the maximum page size.", nameof(members));
        }

        NextContinuationToken = ContractValidation.OptionalText(nextContinuationToken, nameof(nextContinuationToken));
        if (NextContinuationToken is not null
            && System.Text.Encoding.UTF8.GetByteCount(NextContinuationToken) > ClrInspectionContractLimits.MaxContinuationTokenBytes)
        {
            throw new ArgumentException("The continuation token exceeds its maximum size.", nameof(nextContinuationToken));
        }
    }

    public IReadOnlyList<MemberDescriptorDto> Members { get; }

    public string? NextContinuationToken { get; }
}

public sealed class ReadFieldValuesRequestDto
{
    public ReadFieldValuesRequestDto(
        ManagedObjectRefDto @object,
        IEnumerable<MemberRefDto> members)
    {
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
        Members = ContractValidation.CopyRequired(members, nameof(members));
    }

    public ManagedObjectRefDto Object { get; }

    public IReadOnlyList<MemberRefDto> Members { get; }
}

public sealed class ReadFieldValuesResponseDto
{
    public ReadFieldValuesResponseDto(IEnumerable<MemberReadResultDto> results)
    {
        Results = ContractValidation.CopyRequired(results, nameof(results));
        if (Results.Count > ClrInspectionContractLimits.MaxFieldsPerBatch)
        {
            throw new ArgumentException("A field response exceeds the maximum batch size.", nameof(results));
        }
    }

    public IReadOnlyList<MemberReadResultDto> Results { get; }
}

public sealed class ReadPropertyValueRequestDto
{
    public ReadPropertyValueRequestDto(
        ManagedObjectRefDto @object,
        MemberRefDto member)
    {
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
        Member = member ?? throw new ArgumentNullException(nameof(member));
    }

    public ManagedObjectRefDto Object { get; }

    public MemberRefDto Member { get; }
}

public sealed class ReadPropertyValueResponseDto
{
    public ReadPropertyValueResponseDto(MemberReadResultDto result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public MemberReadResultDto Result { get; }
}

public sealed class MemberReadResultDto
{
    public MemberReadResultDto(
        MemberRefDto member,
        ClrReadOutcome outcome,
        ClrValueDto? value = null,
        OperationErrorCode? errorCode = null,
        TargetExceptionDto? targetException = null)
    {
        Member = member ?? throw new ArgumentNullException(nameof(member));
        Outcome = ContractValidation.RequireDefinedEnum(outcome, nameof(outcome));
        if (Outcome == ClrReadOutcome.Available && value is null)
        {
            throw new ArgumentException("An available member result requires a value.", nameof(value));
        }

        if (Outcome != ClrReadOutcome.Available && value is not null)
        {
            throw new ArgumentException("Only an available member result may contain a value.", nameof(value));
        }

        if (Outcome == ClrReadOutcome.TargetFailed && targetException is null)
        {
            throw new ArgumentException("A target failure requires a target exception.", nameof(targetException));
        }

        if (Outcome != ClrReadOutcome.TargetFailed && targetException is not null)
        {
            throw new ArgumentException("Only a target failure may contain a target exception.", nameof(targetException));
        }

        if (Outcome == ClrReadOutcome.Available && errorCode is not null)
        {
            throw new ArgumentException("An available member result cannot contain an error code.", nameof(errorCode));
        }

        if (Outcome == ClrReadOutcome.TargetFailed && errorCode is not null)
        {
            throw new ArgumentException("A target failure cannot contain an operation error code.", nameof(errorCode));
        }

        if (Outcome == ClrReadOutcome.Unavailable && errorCode is null)
        {
            throw new ArgumentException("An unavailable member result requires an error code.", nameof(errorCode));
        }

        Member = member;
        Value = value;
        ErrorCode = errorCode;
        TargetException = targetException;
    }

    public MemberRefDto Member { get; }

    public ClrReadOutcome Outcome { get; }

    public ClrValueDto? Value { get; }

    public OperationErrorCode? ErrorCode { get; }

    public TargetExceptionDto? TargetException { get; }
}

public sealed class TargetExceptionDto
{
    public TargetExceptionDto(TypeIdentityDto exceptionType, bool wasReflectionWrapper)
    {
        ExceptionType = exceptionType ?? throw new ArgumentNullException(nameof(exceptionType));
        WasReflectionWrapper = wasReflectionWrapper;
    }

    public TypeIdentityDto ExceptionType { get; }

    public bool WasReflectionWrapper { get; }
}

public sealed class ClrStructFieldDto
{
    public ClrStructFieldDto(
        string name,
        TypeRefDto fieldType,
        ClrStructFieldOutcome outcome,
        ClrValueDto? value = null)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        FieldType = fieldType ?? throw new ArgumentNullException(nameof(fieldType));
        Outcome = ContractValidation.RequireDefinedEnum(outcome, nameof(outcome));
        if (Outcome == ClrStructFieldOutcome.Available && value is null)
        {
            throw new ArgumentException("An available struct field requires a value.", nameof(value));
        }

        if (Outcome != ClrStructFieldOutcome.Available && value is not null)
        {
            throw new ArgumentException("Only an available struct field may contain a value.", nameof(value));
        }

        Value = value;
    }

    public string Name { get; }

    public TypeRefDto FieldType { get; }

    public ClrStructFieldOutcome Outcome { get; }

    public ClrValueDto? Value { get; }
}

public sealed class ClrValueDto
{
    private ClrValueDto(ClrValueKind kind)
    {
        Kind = ContractValidation.RequireDefinedEnum(kind, nameof(kind));
    }

    public ClrValueKind Kind { get; }

    public bool? BooleanValue { get; private set; }

    public ClrIntegerKind? IntegerKind { get; private set; }

    public string? IntegerValue { get; private set; }

    public ClrFloatingPointKind? FloatingPointKind { get; private set; }

    public string? FloatingPointBits { get; private set; }

    public string? FloatingPointDisplay { get; private set; }

    public string? DecimalDisplay { get; private set; }

    public IReadOnlyList<int>? DecimalBits { get; private set; }

    public ushort? CharCodeUnit { get; private set; }

    public string? StringValue { get; private set; }

    public int? OriginalStringCodeUnitLength { get; private set; }

    public int? ReturnedStringCodeUnitLength { get; private set; }

    public bool? StringTruncated { get; private set; }

    public string? GuidValue { get; private set; }

    public long? DateTimeTicks { get; private set; }

    public DateTimeKind? DateTimeKind { get; private set; }

    public long? DateTimeOffsetClockTicks { get; private set; }

    public short? DateTimeOffsetOffsetMinutes { get; private set; }

    public long? TimeSpanTicks { get; private set; }

    public TypeIdentityDto? EnumType { get; private set; }

    public ClrIntegerKind? EnumUnderlyingKind { get; private set; }

    public string? EnumUnderlyingValue { get; private set; }

    public string? EnumName { get; private set; }

    public TypeIdentityDto? RepresentedType { get; private set; }

    public ManagedObjectRefDto? TypeObjectReference { get; private set; }

    public TypeIdentityDto? ObjectType { get; private set; }

    public ManagedObjectRefDto? ObjectReference { get; private set; }

    public TypeIdentityDto? ValueType { get; private set; }

    public IReadOnlyList<ClrStructFieldDto>? StructFields { get; private set; }

    public bool StructTruncated { get; private set; }

    public bool StructNotExpanded { get; private set; }

    public static ClrValueDto Null()
    {
        return new ClrValueDto(ClrValueKind.Null);
    }

    public static ClrValueDto Boolean(bool value)
    {
        return new ClrValueDto(ClrValueKind.Boolean) { BooleanValue = value };
    }

    public static ClrValueDto Integer(ClrIntegerKind kind, string canonicalValue)
    {
        return new ClrValueDto(ClrValueKind.Integer)
        {
            IntegerKind = ContractValidation.RequireDefinedEnum(kind, nameof(kind)),
            IntegerValue = ContractValidation.RequiredText(canonicalValue, nameof(canonicalValue))
        };
    }

    public static ClrValueDto FloatingPoint(
        ClrFloatingPointKind kind,
        string bits,
        string? display = null)
    {
        return new ClrValueDto(ClrValueKind.FloatingPoint)
        {
            FloatingPointKind = ContractValidation.RequireDefinedEnum(kind, nameof(kind)),
            FloatingPointBits = ContractValidation.RequiredIdentifier(bits, nameof(bits)),
            FloatingPointDisplay = ContractValidation.OptionalText(display, nameof(display))
        };
    }

    public static ClrValueDto Decimal(IEnumerable<int> bits, string? display = null)
    {
        var copy = ContractValidation.CopyRequired(bits, nameof(bits));
        if (copy.Count != 4)
        {
            throw new ArgumentException("A decimal value requires exactly four bit fields.", nameof(bits));
        }

        return new ClrValueDto(ClrValueKind.Decimal)
        {
            DecimalBits = copy,
            DecimalDisplay = ContractValidation.OptionalText(display, nameof(display))
        };
    }

    public static ClrValueDto Char(ushort codeUnit)
    {
        return new ClrValueDto(ClrValueKind.Char) { CharCodeUnit = codeUnit };
    }

    public static ClrValueDto String(
        string value,
        int originalCodeUnitLength,
        int returnedCodeUnitLength,
        bool truncated)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (originalCodeUnitLength < 0 || returnedCodeUnitLength < 0 || returnedCodeUnitLength > originalCodeUnitLength)
        {
            throw new ArgumentOutOfRangeException(nameof(returnedCodeUnitLength));
        }

        if (value.Length != returnedCodeUnitLength)
        {
            throw new ArgumentException("The returned string length must match the value.", nameof(value));
        }

        if (returnedCodeUnitLength > ClrInspectionContractLimits.MaxStringCodeUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(returnedCodeUnitLength));
        }

        if (truncated != (returnedCodeUnitLength < originalCodeUnitLength))
        {
            throw new ArgumentException("String truncation metadata does not match the returned length.", nameof(truncated));
        }

        return new ClrValueDto(ClrValueKind.String)
        {
            StringValue = value,
            OriginalStringCodeUnitLength = originalCodeUnitLength,
            ReturnedStringCodeUnitLength = returnedCodeUnitLength,
            StringTruncated = truncated
        };
    }

    public static ClrValueDto Guid(Guid value)
    {
        return new ClrValueDto(ClrValueKind.Guid) { GuidValue = value.ToString("D", CultureInfo.InvariantCulture) };
    }

    public static ClrValueDto Guid(string value)
    {
        var text = ContractValidation.RequiredIdentifier(value, nameof(value));
        if (!System.Guid.TryParseExact(text, "D", out _))
        {
            throw new ArgumentException("A GUID value must use canonical D-format text.", nameof(value));
        }

        return new ClrValueDto(ClrValueKind.Guid) { GuidValue = text };
    }

    public static ClrValueDto DateTimeValue(long ticks, System.DateTimeKind kind)
    {
        if (ticks < System.DateTime.MinValue.Ticks || ticks > System.DateTime.MaxValue.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        if (!System.Enum.IsDefined(typeof(System.DateTimeKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new ClrValueDto(ClrValueKind.DateTime)
        {
            DateTimeTicks = ticks,
            DateTimeKind = kind
        };
    }

    public static ClrValueDto DateTimeOffsetValue(long clockTicks, short offsetMinutes)
    {
        _ = new DateTimeOffset(
            new System.DateTime(clockTicks, System.DateTimeKind.Unspecified),
            System.TimeSpan.FromMinutes(offsetMinutes));
        return new ClrValueDto(ClrValueKind.DateTimeOffset)
        {
            DateTimeOffsetClockTicks = clockTicks,
            DateTimeOffsetOffsetMinutes = offsetMinutes
        };
    }

    public static ClrValueDto TimeSpanValue(long ticks)
    {
        _ = new System.TimeSpan(ticks);
        return new ClrValueDto(ClrValueKind.TimeSpan) { TimeSpanTicks = ticks };
    }

    public static ClrValueDto EnumValue(
        TypeIdentityDto enumType,
        ClrIntegerKind underlyingKind,
        string underlyingValue,
        string? name = null)
    {
        return new ClrValueDto(ClrValueKind.Enum)
        {
            EnumType = enumType ?? throw new ArgumentNullException(nameof(enumType)),
            EnumUnderlyingKind = ContractValidation.RequireDefinedEnum(underlyingKind, nameof(underlyingKind)),
            EnumUnderlyingValue = ContractValidation.RequiredText(underlyingValue, nameof(underlyingValue)),
            EnumName = ContractValidation.OptionalText(name, nameof(name))
        };
    }

    public static ClrValueDto TypeObject(
        TypeIdentityDto representedType,
        ManagedObjectRefDto? reference = null)
    {
        return new ClrValueDto(ClrValueKind.TypeObject)
        {
            RepresentedType = representedType ?? throw new ArgumentNullException(nameof(representedType)),
            TypeObjectReference = reference
        };
    }

    public static ClrValueDto CreateValueType(
        TypeIdentityDto valueType,
        IEnumerable<ClrStructFieldDto> fields,
        bool truncated,
        bool notExpanded)
    {
        var copy = ContractValidation.CopyRequired(fields, nameof(fields));
        if (copy.Count > ClrInspectionContractLimits.MaxStructFields)
        {
            throw new ArgumentException("A value type exceeds the maximum field count.", nameof(fields));
        }

        if (notExpanded && copy.Count != 0)
        {
            throw new ArgumentException("A non-expanded value type cannot contain expanded fields.", nameof(fields));
        }

        return new ClrValueDto(ClrValueKind.ValueType)
        {
            ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType)),
            StructFields = copy,
            StructTruncated = truncated,
            StructNotExpanded = notExpanded
        };
    }

    public static ClrValueDto CreateObjectReference(
        ManagedObjectRefDto reference,
        TypeIdentityDto objectType)
    {
        return new ClrValueDto(ClrValueKind.ObjectReference)
        {
            ObjectReference = reference ?? throw new ArgumentNullException(nameof(reference)),
            ObjectType = objectType ?? throw new ArgumentNullException(nameof(objectType))
        };
    }
}
