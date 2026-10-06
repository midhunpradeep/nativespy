using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Protocol.Json;

public static class ClrInspectionJsonCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = ProtocolWireConstants.DefaultMaximumJsonDepth,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static JsonElement CreateDescribeObjectPayload(ManagedObjectRefDto @object)
    {
        return SerializeWire(new ClrObjectPayloadWire { Object = ProtocolJsonCodec.ToWire(@object) });
    }

    public static DescribeObjectRequestDto ReadDescribeObjectPayload(JsonElement payload)
    {
        return ReadContract(
            () =>
            {
                var root = ReadObject(payload, "describe-object payload", "object");
                return new DescribeObjectRequestDto(ReadManagedObject(RequiredObject(root, "object")));
            },
            "describe-object payload");
    }

    public static JsonElement SerializeDescribeObjectResponse(DescribeObjectResponseDto response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        return SerializeWire(new DescribeObjectResponseWire
        {
            Object = ProtocolJsonCodec.ToWire(response.Object)
        });
    }

    public static DescribeObjectResponseDto DeserializeDescribeObjectResponse(JsonElement payload)
    {
        var root = ReadObject(payload, "describe-object response", "object");
        try
        {
            return new DescribeObjectResponseDto(ReadManagedObject(RequiredObject(root, "object")));
        }
        catch (Exception exception) when (IsContractFailure(exception))
        {
            throw new ProtocolJsonException("The describe-object response contains invalid CLR identity data.", exception);
        }
    }

    public static JsonElement CreateListMembersPayload(ListMembersRequestDto request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return SerializeWire(new ListMembersRequestWire
        {
            Object = ProtocolJsonCodec.ToWire(request.Object),
            PageSize = request.PageSize,
            Filter = EnumName(request.Filter),
            ContinuationToken = request.ContinuationToken
        });
    }

    public static ListMembersRequestDto ReadListMembersPayload(JsonElement payload)
    {
        return ReadContract(
            () =>
            {
                var root = ReadObject(payload, "list-members payload", "object", "pageSize", "filter", "continuationToken");
                return new ListMembersRequestDto(
                    ReadManagedObject(RequiredObject(root, "object")),
                    RequiredPositiveInt(root, "pageSize"),
                    ParseEnum<ClrMemberKindFilter>(RequiredString(root, "filter"), "filter"),
                    OptionalString(root, "continuationToken"));
            },
            "list-members payload");
    }

    public static JsonElement SerializeListMembersResponse(ListMembersResponseDto response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        return SerializeWire(new ListMembersResponseWire
        {
            Members = response.Members.Select(ToWire).ToArray(),
            NextContinuationToken = response.NextContinuationToken
        });
    }

    public static ListMembersResponseDto DeserializeListMembersResponse(JsonElement payload)
    {
        var root = ReadObject(payload, "list-members response", "members", "nextContinuationToken");
        var wire = Deserialize<ListMembersResponseWire>(root, "list-members response");
        if (wire.Members is null)
        {
            throw new ProtocolJsonException("The list-members response is missing members.");
        }

        try
        {
            return new ListMembersResponseDto(
                ProtocolJsonCollection.MapRequiredElements(
                    wire.Members,
                    member => FromWire(member),
                    "list-members response members"),
                wire.NextContinuationToken);
        }
        catch (Exception exception) when (IsContractFailure(exception))
        {
            throw new ProtocolJsonException("The list-members response contains invalid CLR member data.", exception);
        }
    }

    public static JsonElement CreateReadFieldValuesPayload(ReadFieldValuesRequestDto request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return SerializeWire(new ReadFieldValuesRequestWire
        {
            Object = ProtocolJsonCodec.ToWire(request.Object),
            Members = request.Members.Select(ToWire).ToArray()
        });
    }

    public static ReadFieldValuesRequestDto ReadReadFieldValuesPayload(JsonElement payload)
    {
        return ReadContract(
            () =>
            {
                var root = ReadObject(payload, "read-field-values payload", "object", "members");
                var wire = Deserialize<ReadFieldValuesRequestWire>(root, "read-field-values payload");
                if (wire.Object is null || wire.Members is null)
                {
                    throw new ProtocolJsonException("The read-field-values payload is incomplete.");
                }

                return new ReadFieldValuesRequestDto(
                    ReadManagedObject(wire.Object),
                    ProtocolJsonCollection.MapRequiredElements(
                        wire.Members,
                        member => FromWire(member),
                        "read-field-values members"));
            },
            "read-field-values payload");
    }

    public static JsonElement SerializeReadFieldValuesResponse(ReadFieldValuesResponseDto response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        return SerializeWire(new ReadFieldValuesResponseWire
        {
            Results = response.Results.Select(ToWire).ToArray()
        });
    }

    public static ReadFieldValuesResponseDto DeserializeReadFieldValuesResponse(JsonElement payload)
    {
        var root = ReadObject(payload, "read-field-values response", "results");
        var wire = Deserialize<ReadFieldValuesResponseWire>(root, "read-field-values response");
        if (wire.Results is null)
        {
            throw new ProtocolJsonException("The read-field-values response is missing results.");
        }

        try
        {
            return new ReadFieldValuesResponseDto(
                ProtocolJsonCollection.MapRequiredElements(
                    wire.Results,
                    result => FromWire(result),
                    "read-field-values response results"));
        }
        catch (Exception exception) when (IsContractFailure(exception))
        {
            throw new ProtocolJsonException("The read-field-values response contains invalid CLR value data.", exception);
        }
    }

    public static JsonElement CreateReadPropertyValuePayload(ReadPropertyValueRequestDto request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return SerializeWire(new ReadPropertyValueRequestWire
        {
            Object = ProtocolJsonCodec.ToWire(request.Object),
            Member = ToWire(request.Member)
        });
    }

    public static ReadPropertyValueRequestDto ReadReadPropertyValuePayload(JsonElement payload)
    {
        return ReadContract(
            () =>
            {
                var root = ReadObject(payload, "read-property-value payload", "object", "member");
                var wire = Deserialize<ReadPropertyValueRequestWire>(root, "read-property-value payload");
                if (wire.Object is null || wire.Member is null)
                {
                    throw new ProtocolJsonException("The read-property-value payload is incomplete.");
                }

                return new ReadPropertyValueRequestDto(
                    ReadManagedObject(wire.Object),
                    FromWire(wire.Member));
            },
            "read-property-value payload");
    }

    public static JsonElement SerializeReadPropertyValueResponse(ReadPropertyValueResponseDto response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        return SerializeWire(new ReadPropertyValueResponseWire { Result = ToWire(response.Result) });
    }

    public static ReadPropertyValueResponseDto DeserializeReadPropertyValueResponse(JsonElement payload)
    {
        var root = ReadObject(payload, "read-property-value response", "result");
        var wire = Deserialize<ReadPropertyValueResponseWire>(root, "read-property-value response");
        if (wire.Result is null)
        {
            throw new ProtocolJsonException("The read-property-value response is missing result.");
        }

        try
        {
            return new ReadPropertyValueResponseDto(FromWire(wire.Result));
        }
        catch (Exception exception) when (IsContractFailure(exception))
        {
            throw new ProtocolJsonException("The read-property-value response contains invalid CLR value data.", exception);
        }
    }

    private static MemberDescriptorWire ToWire(MemberDescriptorDto descriptor)
    {
        return new MemberDescriptorWire
        {
            Member = ToWire(descriptor.Member),
            Kind = EnumName(descriptor.Kind),
            Name = descriptor.Name,
            ValueType = new TypeRefWire
            {
                TypeId = descriptor.ValueType.TypeId,
                BoundaryId = descriptor.ValueType.BoundaryId
            },
            DisplaySignature = descriptor.DisplaySignature
        };
    }

    private static MemberDescriptorDto FromWire(MemberDescriptorWire wire)
    {
        if (wire.Member is null || wire.ValueType is null)
        {
            throw new ProtocolJsonException("A member descriptor is incomplete.");
        }

        return new MemberDescriptorDto(
            FromWire(wire.Member),
            ParseEnum<ClrMemberKind>(wire.Kind, "kind"),
            RequiredText(wire.Name, "name"),
            new TypeRefDto(
                RequiredText(wire.ValueType.TypeId, "valueType.typeId"),
                RequiredText(wire.ValueType.BoundaryId, "valueType.boundaryId")),
            wire.DisplaySignature);
    }

    private static MemberRefWire ToWire(MemberRefDto member)
    {
        return new MemberRefWire
        {
            SessionId = member.SessionId,
            MemberId = member.MemberId,
            BoundaryId = member.BoundaryId,
            DeclaringTypeId = member.DeclaringTypeId
        };
    }

    private static MemberRefDto FromWire(MemberRefWire wire)
    {
        return new MemberRefDto(
            RequiredText(wire.SessionId, "member.sessionId"),
            RequiredText(wire.MemberId, "member.memberId"),
            RequiredText(wire.BoundaryId, "member.boundaryId"),
            RequiredText(wire.DeclaringTypeId, "member.declaringTypeId"));
    }

    private static MemberReadResultWire ToWire(MemberReadResultDto result)
    {
        return new MemberReadResultWire
        {
            Member = ToWire(result.Member),
            Outcome = EnumName(result.Outcome),
            Value = result.Value is null ? null : ToWire(result.Value),
            ErrorCode = result.ErrorCode is null ? null : EnumName(result.ErrorCode.Value),
            TargetException = result.TargetException is null ? null : ToWire(result.TargetException)
        };
    }

    private static MemberReadResultDto FromWire(MemberReadResultWire wire)
    {
        if (wire.Member is null)
        {
            throw new ProtocolJsonException("A member read result is missing its member.");
        }

        return new MemberReadResultDto(
            FromWire(wire.Member),
            ParseEnum<ClrReadOutcome>(wire.Outcome, "outcome"),
            wire.Value is null ? null : FromWire(wire.Value),
            wire.ErrorCode is null ? null : ParseEnum<OperationErrorCode>(wire.ErrorCode, "errorCode"),
            wire.TargetException is null ? null : FromWire(wire.TargetException));
    }

    private static TargetExceptionWire ToWire(TargetExceptionDto exception)
    {
        return new TargetExceptionWire
        {
            ExceptionType = ProtocolJsonCodec.ToWire(exception.ExceptionType),
            WasReflectionWrapper = exception.WasReflectionWrapper
        };
    }

    private static TargetExceptionDto FromWire(TargetExceptionWire wire)
    {
        if (wire.ExceptionType is null)
        {
            throw new ProtocolJsonException("A target exception is missing its type.");
        }

        return new TargetExceptionDto(
            ProtocolJsonCodec.FromWire(wire.ExceptionType),
            Required(wire.WasReflectionWrapper, "wasReflectionWrapper"));
    }

    private static StructFieldWire ToWire(ClrStructFieldDto field)
    {
        return new StructFieldWire
        {
            Name = field.Name,
            FieldType = new TypeRefWire
            {
                TypeId = field.FieldType.TypeId,
                BoundaryId = field.FieldType.BoundaryId
            },
            Outcome = EnumName(field.Outcome),
            Value = field.Value is null ? null : ToWire(field.Value)
        };
    }

    private static ClrStructFieldDto FromWire(StructFieldWire wire)
    {
        if (wire.FieldType is null)
        {
            throw new ProtocolJsonException("A struct field is missing its type.");
        }

        return new ClrStructFieldDto(
            RequiredText(wire.Name, "structField.name"),
            new TypeRefDto(
                RequiredText(wire.FieldType.TypeId, "structField.fieldType.typeId"),
                RequiredText(wire.FieldType.BoundaryId, "structField.fieldType.boundaryId")),
            ParseEnum<ClrStructFieldOutcome>(wire.Outcome, "structField.outcome"),
            wire.Value is null ? null : FromWire(wire.Value, nestedValueType: true));
    }

    private static void ValidateValueTypeNesting(ClrValueWire wire, bool nestedValueType)
    {
        if (wire.StructFields is null)
        {
            throw new ProtocolJsonException("A value type is missing structFields.");
        }

        var fields = wire.StructFields;
        if (fields.Length > ClrInspectionContractLimits.MaxStructFields)
        {
            throw new ProtocolJsonException("A value type exceeds the maximum field count.");
        }

        if (nestedValueType
            && (!wire.StructNotExpanded || wire.StructTruncated || fields.Length != 0))
        {
            throw new ProtocolJsonException(
                "Nested value types must be type-only and must not contain expanded fields.");
        }
    }

    private static ClrValueWire ToWire(ClrValueDto value)
    {
        var wire = new ClrValueWire { Kind = EnumName(value.Kind) };
        switch (value.Kind)
        {
            case ClrValueKind.Null:
                break;
            case ClrValueKind.Boolean:
                wire.BooleanValue = value.BooleanValue;
                break;
            case ClrValueKind.Integer:
                wire.IntegerKind = EnumName(value.IntegerKind!.Value);
                wire.IntegerValue = value.IntegerValue;
                break;
            case ClrValueKind.FloatingPoint:
                wire.FloatingPointKind = EnumName(value.FloatingPointKind!.Value);
                wire.FloatingPointBits = value.FloatingPointBits;
                wire.FloatingPointDisplay = value.FloatingPointDisplay;
                break;
            case ClrValueKind.Decimal:
                wire.DecimalBits = value.DecimalBits!.ToArray();
                wire.DecimalDisplay = value.DecimalDisplay;
                break;
            case ClrValueKind.Char:
                wire.CharCodeUnit = value.CharCodeUnit;
                break;
            case ClrValueKind.String:
                wire.StringValue = value.StringValue;
                wire.OriginalStringCodeUnitLength = value.OriginalStringCodeUnitLength;
                wire.ReturnedStringCodeUnitLength = value.ReturnedStringCodeUnitLength;
                wire.StringTruncated = value.StringTruncated;
                break;
            case ClrValueKind.Guid:
                wire.GuidValue = value.GuidValue;
                break;
            case ClrValueKind.DateTime:
                wire.DateTimeTicks = value.DateTimeTicks;
                wire.DateTimeKind = EnumName(value.DateTimeKind!.Value);
                break;
            case ClrValueKind.DateTimeOffset:
                wire.DateTimeOffsetClockTicks = value.DateTimeOffsetClockTicks;
                wire.DateTimeOffsetOffsetMinutes = value.DateTimeOffsetOffsetMinutes;
                break;
            case ClrValueKind.TimeSpan:
                wire.TimeSpanTicks = value.TimeSpanTicks;
                break;
            case ClrValueKind.Enum:
                wire.EnumType = ProtocolJsonCodec.ToWire(value.EnumType!);
                wire.EnumUnderlyingKind = EnumName(value.EnumUnderlyingKind!.Value);
                wire.EnumUnderlyingValue = value.EnumUnderlyingValue;
                wire.EnumName = value.EnumName;
                break;
            case ClrValueKind.TypeObject:
                wire.RepresentedType = ProtocolJsonCodec.ToWire(value.RepresentedType!);
                wire.TypeObjectReference = value.TypeObjectReference is null
                    ? null
                    : ProtocolJsonCodec.ToWire(value.TypeObjectReference);
                break;
            case ClrValueKind.ValueType:
                wire.ValueType = ProtocolJsonCodec.ToWire(value.ValueType!);
                wire.StructFields = value.StructFields!.Select(ToWire).ToArray();
                wire.StructTruncated = value.StructTruncated;
                wire.StructNotExpanded = value.StructNotExpanded;
                break;
            case ClrValueKind.ObjectReference:
                wire.ObjectType = ProtocolJsonCodec.ToWire(value.ObjectType!);
                wire.ObjectReference = ProtocolJsonCodec.ToWire(value.ObjectReference!);
                break;
            default:
                throw new ProtocolJsonException("The CLR value kind is not supported.");
        }

        return wire;
    }

    private static ClrValueDto FromWire(ClrValueWire wire)
    {
        return FromWire(wire, nestedValueType: false);
    }

    private static ClrValueDto FromWire(ClrValueWire wire, bool nestedValueType)
    {
        var kind = ParseEnum<ClrValueKind>(wire.Kind, "kind");
        if (kind == ClrValueKind.ValueType)
        {
            ValidateValueTypeNesting(wire, nestedValueType);
        }

        return kind switch
        {
            ClrValueKind.Null => ClrValueDto.Null(),
            ClrValueKind.Boolean => ClrValueDto.Boolean(Required(wire.BooleanValue, "booleanValue")),
            ClrValueKind.Integer => ClrValueDto.Integer(
                ParseEnum<ClrIntegerKind>(RequiredText(wire.IntegerKind, "integerKind"), "integerKind"),
                RequiredText(wire.IntegerValue, "integerValue")),
            ClrValueKind.FloatingPoint => ClrValueDto.FloatingPoint(
                ParseEnum<ClrFloatingPointKind>(RequiredText(wire.FloatingPointKind, "floatingPointKind"), "floatingPointKind"),
                RequiredText(wire.FloatingPointBits, "floatingPointBits"),
                wire.FloatingPointDisplay),
            ClrValueKind.Decimal => ClrValueDto.Decimal(
                wire.DecimalBits ?? throw new ProtocolJsonException("A decimal value is missing its bits."),
                wire.DecimalDisplay),
            ClrValueKind.Char => ClrValueDto.Char(Required(wire.CharCodeUnit, "charCodeUnit")),
            ClrValueKind.String => ClrValueDto.String(
                wire.StringValue ?? throw new ProtocolJsonException("A string value is missing its text."),
                Required(wire.OriginalStringCodeUnitLength, "originalStringCodeUnitLength"),
                Required(wire.ReturnedStringCodeUnitLength, "returnedStringCodeUnitLength"),
                Required(wire.StringTruncated, "stringTruncated")),
            ClrValueKind.Guid => ClrValueDto.Guid(RequiredText(wire.GuidValue, "guidValue")),
            ClrValueKind.DateTime => ClrValueDto.DateTimeValue(
                Required(wire.DateTimeTicks, "dateTimeTicks"),
                ParseEnum<System.DateTimeKind>(RequiredText(wire.DateTimeKind, "dateTimeKind"), "dateTimeKind")),
            ClrValueKind.DateTimeOffset => ClrValueDto.DateTimeOffsetValue(
                Required(wire.DateTimeOffsetClockTicks, "dateTimeOffsetClockTicks"),
                Required(wire.DateTimeOffsetOffsetMinutes, "dateTimeOffsetOffsetMinutes")),
            ClrValueKind.TimeSpan => ClrValueDto.TimeSpanValue(Required(wire.TimeSpanTicks, "timeSpanTicks")),
            ClrValueKind.Enum => ClrValueDto.EnumValue(
                ProtocolJsonCodec.FromWire(wire.EnumType ?? throw new ProtocolJsonException("An enum value is missing its type.")),
                ParseEnum<ClrIntegerKind>(RequiredText(wire.EnumUnderlyingKind, "enumUnderlyingKind"), "enumUnderlyingKind"),
                RequiredText(wire.EnumUnderlyingValue, "enumUnderlyingValue"),
                wire.EnumName),
            ClrValueKind.TypeObject => ClrValueDto.TypeObject(
                ProtocolJsonCodec.FromWire(wire.RepresentedType ?? throw new ProtocolJsonException("A type object is missing its represented type.")),
                wire.TypeObjectReference is null ? null : ProtocolJsonCodec.FromWire(wire.TypeObjectReference)),
            ClrValueKind.ValueType => ClrValueDto.CreateValueType(
                ProtocolJsonCodec.FromWire(wire.ValueType ?? throw new ProtocolJsonException("A value type is missing its type.")),
                ProtocolJsonCollection.MapRequiredElements(
                    wire.StructFields!,
                    field => FromWire(field),
                    "value type structFields"),
                wire.StructTruncated,
                wire.StructNotExpanded),
            ClrValueKind.ObjectReference => ClrValueDto.CreateObjectReference(
                ProtocolJsonCodec.FromWire(wire.ObjectReference ?? throw new ProtocolJsonException("An object reference is missing its reference.")),
                ProtocolJsonCodec.FromWire(wire.ObjectType ?? throw new ProtocolJsonException("An object reference is missing its type."))),
            _ => throw new ProtocolJsonException("The CLR value kind is not supported.")
        };
    }

    private static JsonElement SerializeWire<T>(T value)
    {
        try
        {
            return JsonSerializer.SerializeToDocument(value, SerializerOptions).RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new ProtocolJsonException("The CLR inspection value could not be serialized.", exception);
        }
    }

    private static T ReadContract<T>(Func<T> reader, string description)
    {
        try
        {
            return reader();
        }
        catch (ProtocolJsonException)
        {
            throw;
        }
        catch (Exception exception) when (IsContractFailure(exception))
        {
            throw new ProtocolJsonException($"The {description} contains invalid contract data.", exception);
        }
    }

    private static bool IsContractFailure(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or OverflowException
            or FormatException;
    }

    private static T Deserialize<T>(JsonElement root, string description)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(root.GetRawText(), SerializerOptions)
                ?? throw new ProtocolJsonException($"The {description} was null.");
        }
        catch (ProtocolJsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ProtocolJsonException($"The {description} is invalid.", exception);
        }
    }

    private static JsonElement ReadObject(JsonElement payload, string description, params string[] allowed)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new ProtocolJsonException($"The {description} must be an object.");
        }

        ValidateNoDuplicateProperties(payload);
        ValidateClrValueShapes(payload);
        EnsureOnlyProperties(payload, description, allowed);
        return payload;
    }

    private static ManagedObjectRefDto ReadManagedObject(JsonElement element)
    {
        var root = ReadObject(element, "managed object", "handle", "typeIdentity", "boundaryId", "contextId");
        var wire = Deserialize<ManagedObjectWire>(root, "managed object");
        if (wire.Handle is null)
        {
            throw new ProtocolJsonException("A managed object is missing its handle.");
        }

        return ProtocolJsonCodec.FromWire(wire);
    }

    private static ManagedObjectRefDto ReadManagedObject(ManagedObjectWire wire)
    {
        if (wire is null || wire.Handle is null)
        {
            throw new ProtocolJsonException("A managed object is missing its handle.");
        }

        return ProtocolJsonCodec.FromWire(wire);
    }

    private static JsonElement RequiredObject(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new ProtocolJsonException($"Required object property '{name}' is missing or invalid.");
        }

        return value;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ProtocolJsonException($"Required string property '{name}' is missing or invalid.");
        }

        return RequiredText(value.GetString(), name);
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ProtocolJsonException($"Optional string property '{name}' is invalid.");
        }

        return value.GetString();
    }

    private static int RequiredPositiveInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result <= 0)
        {
            throw new ProtocolJsonException($"Required positive integer property '{name}' is invalid.");
        }

        return result;
    }

    private static string RequiredText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProtocolJsonException($"Required text property '{name}' is empty.");
        }

        return value!;
    }

    private static T Required<T>(T? value, string name)
        where T : struct
    {
        return value ?? throw new ProtocolJsonException($"Required value property '{name}' is missing.");
    }

    private static void EnsureOnlyProperties(JsonElement root, string description, params string[] allowedNames)
    {
        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ProtocolJsonException($"The {description} contains unknown property '{property.Name}'.");
            }
        }
    }

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ProtocolJsonException($"The JSON object contains duplicate property '{property.Name}'.");
                }

                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateNoDuplicateProperties(item);
            }
        }
    }

    private static void ValidateClrValueShapes(JsonElement element)
    {
        ValidateClrValueShapes(element, nestedValueType: false);
    }

    private static void ValidateClrValueShapes(JsonElement element, bool nestedValueType)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            ClrValueKind? kind = null;
            if (element.TryGetProperty("kind", out var kindElement)
                && kindElement.ValueKind == JsonValueKind.String
                && Enum.TryParse<ClrValueKind>(kindElement.GetString(), ignoreCase: false, out var parsedKind)
                && Enum.IsDefined(typeof(ClrValueKind), parsedKind))
            {
                kind = parsedKind;
                var allowed = parsedKind switch
                {
                    ClrValueKind.Null => new[] { "kind" },
                    ClrValueKind.Boolean => new[] { "kind", "booleanValue" },
                    ClrValueKind.Integer => new[] { "kind", "integerKind", "integerValue" },
                    ClrValueKind.FloatingPoint => new[] { "kind", "floatingPointKind", "floatingPointBits", "floatingPointDisplay" },
                    ClrValueKind.Decimal => new[] { "kind", "decimalBits", "decimalDisplay" },
                    ClrValueKind.Char => new[] { "kind", "charCodeUnit" },
                    ClrValueKind.String => new[] { "kind", "stringValue", "originalStringCodeUnitLength", "returnedStringCodeUnitLength", "stringTruncated" },
                    ClrValueKind.Guid => new[] { "kind", "guidValue" },
                    ClrValueKind.DateTime => new[] { "kind", "dateTimeTicks", "dateTimeKind" },
                    ClrValueKind.DateTimeOffset => new[] { "kind", "dateTimeOffsetClockTicks", "dateTimeOffsetOffsetMinutes" },
                    ClrValueKind.TimeSpan => new[] { "kind", "timeSpanTicks" },
                    ClrValueKind.Enum => new[] { "kind", "enumType", "enumUnderlyingKind", "enumUnderlyingValue", "enumName" },
                    ClrValueKind.TypeObject => new[] { "kind", "representedType", "typeObjectReference" },
                    ClrValueKind.ValueType => new[] { "kind", "valueType", "structFields", "structTruncated", "structNotExpanded" },
                    ClrValueKind.ObjectReference => new[] { "kind", "objectType", "objectReference" },
                    _ => throw new ProtocolJsonException("The CLR value kind is not supported.")
                };
                EnsureOnlyProperties(element, "CLR value", allowed);

                if (parsedKind == ClrValueKind.ValueType)
                {
                    ValidateValueTypeElement(element, nestedValueType);
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (kind == ClrValueKind.ValueType
                    && property.Name == "structFields"
                    && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in property.Value.EnumerateArray())
                    {
                        if (field.ValueKind == JsonValueKind.Object
                            && field.TryGetProperty("value", out var fieldValue))
                        {
                            ValidateClrValueShapes(fieldValue, nestedValueType: true);
                        }
                        else
                        {
                            ValidateClrValueShapes(field, nestedValueType: false);
                        }
                    }

                    continue;
                }

                ValidateClrValueShapes(property.Value, nestedValueType: false);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateClrValueShapes(item, nestedValueType: false);
            }
        }
    }

    private static void ValidateValueTypeElement(JsonElement element, bool nestedValueType)
    {
        if (Encoding.UTF8.GetByteCount(element.GetRawText()) > ClrInspectionContractLimits.MaxStructPayloadBytes)
        {
            throw new ProtocolJsonException("A value type exceeds the maximum payload size.");
        }

        if (!element.TryGetProperty("structFields", out var fieldsElement))
        {
            throw new ProtocolJsonException("A value type is missing structFields.");
        }

        if (fieldsElement.ValueKind != JsonValueKind.Array)
        {
            throw new ProtocolJsonException("A value type structFields property must be an array.");
        }

        var fields = fieldsElement.EnumerateArray().ToArray();

        if (fields.Length > ClrInspectionContractLimits.MaxStructFields)
        {
            throw new ProtocolJsonException("A value type exceeds the maximum field count.");
        }

        var notExpanded = element.TryGetProperty("structNotExpanded", out var notExpandedElement)
            && notExpandedElement.ValueKind == JsonValueKind.True;
        var truncated = element.TryGetProperty("structTruncated", out var truncatedElement)
            && truncatedElement.ValueKind == JsonValueKind.True;
        if (nestedValueType
            && (!notExpanded || truncated || fields.Length != 0))
        {
            throw new ProtocolJsonException(
                "Nested value types must be type-only and must not contain expanded fields.");
        }

        if (notExpanded && fields.Length != 0)
        {
            throw new ProtocolJsonException(
                "A non-expanded value type cannot contain expanded fields.");
        }
    }

    private static string EnumName<TEnum>(TEnum value)
        where TEnum : struct
    {
        var name = System.Enum.GetName(typeof(TEnum), value);
        return name ?? throw new ProtocolJsonException($"The enum value '{value}' is not defined.");
    }

    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct
    {
        if (!System.Enum.TryParse<TEnum>(value, ignoreCase: false, out var result)
            || !System.Enum.IsDefined(typeof(TEnum), result))
        {
            throw new ProtocolJsonException($"The enum property '{propertyName}' has an unknown value.");
        }

        return result;
    }
}
