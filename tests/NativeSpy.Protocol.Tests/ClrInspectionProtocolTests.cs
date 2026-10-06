using System.Text.Json;
using System.Text.Json.Nodes;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using NativeSpy.Protocol.Json;
using Xunit;

namespace NativeSpy.Protocol.Tests;

public sealed class ClrInspectionProtocolTests
{
    [Theory]
    [InlineData(OperationErrorCode.TargetTimeout)]
    [InlineData(OperationErrorCode.ObjectCollected)]
    [InlineData(OperationErrorCode.InvalidContinuation)]
    public void Unavailable_member_results_reject_top_level_error_codes(OperationErrorCode code)
    {
        var member = new MemberRefDto("session", "member", "boundary", "type");

        Assert.Throws<ArgumentException>(() =>
            new MemberReadResultDto(
                member,
                ClrReadOutcome.Unavailable,
                errorCode: code));
    }

    [Fact]
    public void Unsupported_member_results_cannot_carry_an_error_code()
    {
        var member = new MemberRefDto("session", "member", "boundary", "type");

        Assert.Throws<ArgumentException>(() =>
            new MemberReadResultDto(
                member,
                ClrReadOutcome.Unsupported,
                errorCode: OperationErrorCode.MemberUnavailable));
    }

    [Fact]
    public void List_members_response_exposes_no_total_cardinality_field()
    {
        var response = new ListMembersResponseDto(
            new[]
            {
                new MemberDescriptorDto(
                    new MemberRefDto("session", "member", "boundary", "type"),
                    ClrMemberKind.Field,
                    "Field",
                    new TypeRefDto("int", "boundary"),
                    "System.Int32 Field")
            },
            "continuation");

        using var document = JsonDocument.Parse(
            ClrInspectionJsonCodec.SerializeListMembersResponse(response).GetRawText());
        var propertyNames = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(new[] { "members", "nextContinuationToken" }, propertyNames);
        Assert.DoesNotContain(propertyNames, name =>
            string.Equals(name, "total", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "totalCount", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "memberCount", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Clr_values_round_trip_with_their_closed_union_branch()
    {
        var member = new MemberRefDto("session", "member", "boundary", "type");
        var decimalBits = decimal.GetBits(12.5m);
        var response = new ReadFieldValuesResponseDto(new[]
        {
            new MemberReadResultDto(
                member,
                ClrReadOutcome.Available,
                ClrValueDto.Decimal(decimalBits, "12.5"))
        });

        var payload = ClrInspectionJsonCodec.SerializeReadFieldValuesResponse(response);
        var decoded = ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(payload);

        Assert.Equal("12.5", decoded.Results[0].Value!.DecimalDisplay);
        Assert.Equal(decimalBits, decoded.Results[0].Value!.DecimalBits);
    }

    [Fact]
    public void Wire_codec_round_trips_all_scalar_categories_and_shallow_references()
    {
        var type = new TypeIdentityDto(
            "type",
            "Example.Type",
            "Example",
            "boundary",
            isValueType: false,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());
        var reference = new ManagedObjectRefDto(
            new HandleRefDto("session", "object", 1, HandleKind.ClrObject, "boundary"),
            type,
            "boundary");
        var decimalBits = decimal.GetBits(-123.45m);
        var values = new List<ClrValueDto>
        {
            ClrValueDto.Null(),
            ClrValueDto.Boolean(true),
            ClrValueDto.Integer(ClrIntegerKind.Int32, "-2147483648"),
            ClrValueDto.FloatingPoint(ClrFloatingPointKind.Single, "FFC00000", "NaN"),
            ClrValueDto.FloatingPoint(ClrFloatingPointKind.Double, "8000000000000000", "-0"),
            ClrValueDto.Decimal(decimalBits, "-123.45"),
            ClrValueDto.Char(ushort.MaxValue),
            ClrValueDto.String(new string('x', 4095), 4096, 4095, truncated: true),
            ClrValueDto.Guid("00112233-4455-6677-8899-aabbccddeeff"),
            ClrValueDto.DateTimeValue(123, DateTimeKind.Local),
            ClrValueDto.DateTimeOffsetValue(new DateTime(2020, 1, 2).Ticks, -840),
            ClrValueDto.TimeSpanValue(-123456789),
            ClrValueDto.EnumValue(type, ClrIntegerKind.UInt64, "18446744073709551615", "Max"),
            ClrValueDto.TypeObject(type, reference),
            ClrValueDto.CreateValueType(
                type,
                new[]
                {
                    new ClrStructFieldDto(
                        "Number",
                        new TypeRefDto("int", "boundary"),
                        ClrStructFieldOutcome.Available,
                        ClrValueDto.Integer(ClrIntegerKind.Int32, "7"))
                },
                truncated: false,
                notExpanded: false),
            ClrValueDto.CreateObjectReference(reference, type)
        };
        var response = new ReadFieldValuesResponseDto(
            values.Select((value, index) => new MemberReadResultDto(
                new MemberRefDto("session", "member-" + index, "boundary", "type"),
                ClrReadOutcome.Available,
                value)));

        var decoded = ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(
            ClrInspectionJsonCodec.SerializeReadFieldValuesResponse(response));
        var decodedValues = decoded.Results.Select(result => result.Value!).ToArray();

        Assert.Equal(values.Count, decodedValues.Length);
        Assert.Equal(
            new[]
            {
                ClrValueKind.Null,
                ClrValueKind.Boolean,
                ClrValueKind.Integer,
                ClrValueKind.FloatingPoint,
                ClrValueKind.FloatingPoint,
                ClrValueKind.Decimal,
                ClrValueKind.Char,
                ClrValueKind.String,
                ClrValueKind.Guid,
                ClrValueKind.DateTime,
                ClrValueKind.DateTimeOffset,
                ClrValueKind.TimeSpan,
                ClrValueKind.Enum,
                ClrValueKind.TypeObject,
                ClrValueKind.ValueType,
                ClrValueKind.ObjectReference
            },
            decodedValues.Select(value => value.Kind));
        Assert.Equal("FFC00000", decodedValues[3].FloatingPointBits);
        Assert.Equal("8000000000000000", decodedValues[4].FloatingPointBits);
        Assert.Equal(decimalBits, decodedValues[5].DecimalBits);
        Assert.Equal(DateTimeKind.Local, decodedValues[9].DateTimeKind);
        Assert.Equal((short)-840, decodedValues[10].DateTimeOffsetOffsetMinutes);
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", decodedValues[8].GuidValue);
        Assert.Equal("18446744073709551615", decodedValues[12].EnumUnderlyingValue);
        Assert.Equal(ClrValueKind.ValueType, decodedValues[14].Kind);
        Assert.Equal(ClrValueKind.ObjectReference, decodedValues[15].Kind);
    }

    [Fact]
    public void Enum_underlying_widths_are_checked_independently()
    {
        var type = new TypeIdentityDto(
            "enum",
            "Example.Enum",
            "Example",
            "boundary",
            isValueType: true,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());
        var kinds = new[]
        {
            (ClrIntegerKind.SByte, "-128"),
            (ClrIntegerKind.Byte, "255"),
            (ClrIntegerKind.Int16, "-32768"),
            (ClrIntegerKind.UInt16, "65535"),
            (ClrIntegerKind.Int32, "-2147483648"),
            (ClrIntegerKind.UInt32, "4294967295"),
            (ClrIntegerKind.Int64, "-9223372036854775808"),
            (ClrIntegerKind.UInt64, "18446744073709551615")
        };
        var response = new ReadFieldValuesResponseDto(
            kinds.Select((item, index) => new MemberReadResultDto(
                new MemberRefDto("session", "enum-" + index, "boundary", "enum"),
                ClrReadOutcome.Available,
                ClrValueDto.EnumValue(type, item.Item1, item.Item2))));

        var decoded = ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(
            ClrInspectionJsonCodec.SerializeReadFieldValuesResponse(response));

        Assert.Equal(kinds.Select(item => item.Item1), decoded.Results.Select(result => result.Value!.EnumUnderlyingKind!.Value));
        Assert.Equal(kinds.Select(item => item.Item2), decoded.Results.Select(result => result.Value!.EnumUnderlyingValue));
    }

    [Theory]
    [InlineData("SByte", "-128")]
    [InlineData("SByte", "127")]
    [InlineData("Byte", "0")]
    [InlineData("Byte", "255")]
    [InlineData("Int16", "-32768")]
    [InlineData("Int16", "32767")]
    [InlineData("UInt16", "0")]
    [InlineData("UInt16", "65535")]
    [InlineData("Int32", "-2147483648")]
    [InlineData("Int32", "2147483647")]
    [InlineData("UInt32", "0")]
    [InlineData("UInt32", "4294967295")]
    [InlineData("Int64", "-9223372036854775808")]
    [InlineData("Int64", "9223372036854775807")]
    [InlineData("UInt64", "0")]
    [InlineData("UInt64", "18446744073709551615")]
    public void Integer_values_accept_canonical_extrema(string kind, string text)
    {
        var decoded = DecodeValue($"{{\"kind\":\"Integer\",\"integerKind\":\"{kind}\",\"integerValue\":\"{text}\"}}");

        Assert.Equal(ClrValueKind.Integer, decoded.Kind);
        Assert.Equal(text, decoded.IntegerValue);
    }

    [Theory]
    [InlineData("Byte", "-1")]
    [InlineData("Byte", "256")]
    [InlineData("Int16", "32768")]
    [InlineData("UInt16", "-1")]
    [InlineData("Int32", "2147483648")]
    [InlineData("UInt64", "18446744073709551616")]
    [InlineData("Int32", "001")]
    [InlineData("Int32", "+1")]
    [InlineData("Int32", "-0")]
    [InlineData("Int32", "１２")]
    public void Integer_values_reject_noncanonical_text_and_wrong_ranges(string kind, string text)
    {
        AssertInvalidValue($"{{\"kind\":\"Integer\",\"integerKind\":\"{kind}\",\"integerValue\":\"{text}\"}}");
    }

    [Fact]
    public void Enum_underlying_values_use_the_same_integer_validation()
    {
        AssertInvalidValue(EnumJson("Int32", "2147483648"));
        var decoded = DecodeValue(EnumJson("UInt16", "65535"));
        Assert.Equal("65535", decoded.EnumUnderlyingValue);
    }

    [Theory]
    [InlineData("Single", "00000000")]
    [InlineData("Single", "80000000")]
    [InlineData("Single", "7F800000")]
    [InlineData("Double", "FFF8000000000001")]
    [InlineData("Double", "7FF0000000000000")]
    public void Floating_point_values_preserve_exact_bits(string kind, string bits)
    {
        var decoded = DecodeValue(
            $"{{\"kind\":\"FloatingPoint\",\"floatingPointKind\":\"{kind}\",\"floatingPointBits\":\"{bits}\",\"floatingPointDisplay\":\"ignored\"}}");

        Assert.Equal(bits, decoded.FloatingPointBits);
    }

    [Theory]
    [InlineData("Single", "0000000")]
    [InlineData("Single", "000000000")]
    [InlineData("Double", "00000000")]
    [InlineData("Double", "000000000000000G")]
    [InlineData("Double", "0x0000000000000000")]
    public void Floating_point_values_reject_wrong_width_and_alternative_encodings(string kind, string bits)
    {
        AssertInvalidValue(
            $"{{\"kind\":\"FloatingPoint\",\"floatingPointKind\":\"{kind}\",\"floatingPointBits\":\"{bits}\"}}");
    }

    [Fact]
    public void Decimal_values_accept_legal_flags_and_reject_illegal_flags()
    {
        var valid = decimal.GetBits(1.5m);
        var decoded = DecodeValue(
            $"{{\"kind\":\"Decimal\",\"decimalBits\":[{string.Join(',', valid)}]}}");
        Assert.Equal(valid, decoded.DecimalBits);

        AssertInvalidValue("{\"kind\":\"Decimal\",\"decimalBits\":[0,0,0,1900544]}");
        AssertInvalidValue("{\"kind\":\"Decimal\",\"decimalBits\":[0,0,0,8388608]}");
    }

    [Fact]
    public void String_limits_and_truncation_metadata_are_semantically_checked()
    {
        var exact = new string('a', ClrInspectionContractLimits.MaxStringCodeUnits);
        var decoded = DecodeValue(
            $"{{\"kind\":\"String\",\"stringValue\":{JsonSerializer.Serialize(exact)},\"originalStringCodeUnitLength\":4096,\"returnedStringCodeUnitLength\":4096,\"stringTruncated\":false}}");
        Assert.False(decoded.StringTruncated);

        const string highSurrogate = "\"\\uD800\"";
        AssertInvalidValue(
            $"{{\"kind\":\"String\",\"stringValue\":{highSurrogate},\"originalStringCodeUnitLength\":2,\"returnedStringCodeUnitLength\":1,\"stringTruncated\":true}}");
        AssertInvalidValue(
            $"{{\"kind\":\"String\",\"stringValue\":\"x\",\"originalStringCodeUnitLength\":2,\"returnedStringCodeUnitLength\":1,\"stringTruncated\":false}}");
    }

    [Fact]
    public void Value_types_allow_one_expanded_level_and_type_only_nested_structs()
    {
        var nested = ValueTypeJson("Nested", string.Empty, truncated: false, notExpanded: true);
        var root = ValueTypeJson(
            "Root",
            StructFieldJson("Nested", nested) + "," + StructFieldJson("Number", "{\"kind\":\"Integer\",\"integerKind\":\"Int32\",\"integerValue\":\"7\"}"));

        var decoded = DecodeValue(root);
        Assert.Equal(2, decoded.StructFields!.Count);
        var nestedValue = decoded.StructFields[0].Value!;
        Assert.True(nestedValue.StructNotExpanded);
        Assert.Empty(nestedValue.StructFields!);
    }

    [Fact]
    public void Value_types_reject_recursive_expansion_field_count_and_payload_limits()
    {
        var expandedNested = ValueTypeJson(
            "Nested",
            StructFieldJson("Number", "{\"kind\":\"Null\"}"),
            truncated: false,
            notExpanded: false);
        AssertInvalidValue(ValueTypeJson("Root", StructFieldJson("Nested", expandedNested)));

        var tooManyFields = string.Join(
            ",",
            Enumerable.Range(0, ClrInspectionContractLimits.MaxStructFields + 1)
                .Select(index => StructFieldJson($"Field{index}", "{\"kind\":\"Null\"}")));
        AssertInvalidValue(ValueTypeJson("Root", tooManyFields));

        var veryLargeTypeName = new string('x', ClrInspectionContractLimits.MaxStructPayloadBytes);
        AssertInvalidValue(ValueTypeJson("Root", string.Empty, typeFullName: veryLargeTypeName));
    }

    [Fact]
    public void Arrays_and_collections_are_represented_only_as_object_references()
    {
        var decoded = DecodeValue(
            $"{{\"kind\":\"ObjectReference\",\"objectType\":{TypeIdentityJson("Array", isValueType: false)},\"objectReference\":{ManagedObjectJson()}}}");

        Assert.Equal(ClrValueKind.ObjectReference, decoded.Kind);
        Assert.NotNull(decoded.ObjectReference);
    }

    [Fact]
    public void Null_member_elements_are_rejected_in_all_clr_collection_shapes()
    {
        using var readFieldRequest = JsonDocument.Parse(
            "{\"object\":"
            + ManagedObjectJson()
            + ",\"members\":[null]}" );
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.ReadReadFieldValuesPayload(readFieldRequest.RootElement));

        using var listMembersResponse = JsonDocument.Parse("{\"members\":[null]}");
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeListMembersResponse(listMembersResponse.RootElement));

        using var readFieldResponse = JsonDocument.Parse("{\"results\":[null]}");
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(readFieldResponse.RootElement));

        AssertInvalidValue(ValueTypeJson("Root", "null"));
        var typeWithNullGenericArgument = TypeIdentityJson("Reference", isValueType: false)
            .Replace("\"genericArguments\":[]", "\"genericArguments\":[null]", StringComparison.Ordinal);
        AssertInvalidValue(
            $"{{\"kind\":\"ObjectReference\",\"objectType\":{typeWithNullGenericArgument},\"objectReference\":{ManagedObjectJson()}}}");
    }

    [Fact]
    public void Type_identity_collections_require_presence_across_real_clr_value_shapes()
    {
        var enumType = TypeIdentityJson("Enum", isValueType: true);
        Assert.Equal(ClrValueKind.Enum, DecodeValue(EnumJsonWithType(enumType)).Kind);
        AssertInvalidValue(EnumJsonWithType(RemoveEmptyArrayProperty(enumType, "genericArguments")));
        AssertInvalidValue(EnumJsonWithType(ReplaceEmptyArrayWithNull(enumType, "genericArguments")));
        AssertInvalidValue(EnumJsonWithType(ReplaceEmptyArrayWithNullElement(enumType, "genericArguments")));

        var objectType = TypeIdentityJson("Object", isValueType: false);
        Assert.Equal(ClrValueKind.ObjectReference, DecodeValue(ObjectReferenceValueJson(objectType)).Kind);
        AssertInvalidValue(ObjectReferenceValueJson(RemoveEmptyArrayProperty(objectType, "interfaces")));
        AssertInvalidValue(ObjectReferenceValueJson(ReplaceEmptyArrayWithNull(objectType, "interfaces")));
        AssertInvalidValue(ObjectReferenceValueJson(ReplaceEmptyArrayWithNullElement(objectType, "interfaces")));

        var representedType = TypeIdentityJson("Represented", isValueType: false);
        AssertInvalidValue(TypeObjectValueJson(RemoveEmptyArrayProperty(representedType, "genericArguments")));
        AssertInvalidValue(TypeObjectValueJson(RemoveEmptyArrayProperty(representedType, "interfaces")));

        using var describeMissing = JsonDocument.Parse(
            DescribeResponseJson(RemoveEmptyArrayProperty(representedType, "genericArguments")));
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeDescribeObjectResponse(describeMissing.RootElement));
    }

    [Fact]
    public void Value_type_struct_fields_require_presence_but_accept_explicit_empty_and_bounded_fields()
    {
        var typeOnly = ValueTypeJson("Root", string.Empty, notExpanded: true);
        var decodedTypeOnly = DecodeValue(typeOnly);
        Assert.Empty(decodedTypeOnly.StructFields!);
        Assert.True(decodedTypeOnly.StructNotExpanded);

        var expanded = ValueTypeJson(
            "Root",
            StructFieldJson("Number", "{\"kind\":\"Integer\",\"integerKind\":\"Int32\",\"integerValue\":\"7\"}"));
        Assert.Single(DecodeValue(expanded).StructFields!);

        AssertInvalidValue(RemoveEmptyArrayProperty(typeOnly, "structFields"));
        AssertInvalidValue(ReplaceEmptyArrayWithNull(typeOnly, "structFields"));
        AssertInvalidValue(ReplaceEmptyArrayWithNullElement(typeOnly, "structFields"));
    }

    [Fact]
    public void List_members_page_size_is_required_at_the_wire_boundary()
    {
        using var document = JsonDocument.Parse(
            $"{{\"object\":{ManagedObjectJson()},\"filter\":\"All\",\"continuationToken\":null}}");

        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.ReadListMembersPayload(document.RootElement));
    }

    [Fact]
    public void Type_identity_is_value_type_requires_wire_presence_in_all_common_clr_shapes()
    {
        var explicitFalse = TypeIdentityJson("Reference", isValueType: false);
        var explicitTrue = TypeIdentityJson("Value", isValueType: true);
        Assert.False(DecodeValue(EnumJsonWithType(explicitFalse)).EnumType!.IsValueType);
        Assert.True(DecodeValue(EnumJsonWithType(explicitTrue)).EnumType!.IsValueType);

        var serializedType = new TypeIdentityDto(
            "reference",
            "Reference",
            "assembly",
            "boundary",
            isValueType: false,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());
        var serializedReference = new ManagedObjectRefDto(
            new HandleRefDto("session", "object", 1, HandleKind.ClrObject, "boundary"),
            serializedType,
            "boundary");
        var serializedDescribe = ClrInspectionJsonCodec.SerializeDescribeObjectResponse(
            new DescribeObjectResponseDto(serializedReference));
        Assert.Contains("\"isValueType\":false", serializedDescribe.GetRawText(), StringComparison.Ordinal);

        Assert.Equal(ClrValueKind.ObjectReference, DecodeValue(ObjectReferenceValueJson(explicitFalse)).Kind);
        Assert.Equal(ClrValueKind.TypeObject, DecodeValue(TypeObjectValueJson(explicitFalse)).Kind);
        Assert.Equal(ClrValueKind.ValueType, DecodeValue(ValueTypeJson("Value", string.Empty, notExpanded: true)).Kind);

        var missing = RemoveProperty(explicitFalse, "isValueType");
        AssertInvalidValue(EnumJsonWithType(missing));
        AssertInvalidValue(ObjectReferenceValueJson(missing));
        AssertInvalidValue(TypeObjectValueJson(missing));
        AssertInvalidValue(
            $"{{\"kind\":\"ValueType\",\"valueType\":{missing},\"structFields\":[],\"structTruncated\":false,\"structNotExpanded\":true}}");

        using var describe = JsonDocument.Parse(DescribeResponseJson(missing));
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeDescribeObjectResponse(describe.RootElement));

        using var targetFailure = JsonDocument.Parse(TargetFailedResponse(missing, wasReflectionWrapper: false));
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadPropertyValueResponse(targetFailure.RootElement));
    }

    [Fact]
    public void Target_exception_wrapper_flag_requires_presence_and_preserves_both_boolean_values()
    {
        var exceptionType = TypeIdentityJson("Exception", isValueType: false);
        var notWrapped = DeserializeTargetFailure(
            TargetFailedResponse(exceptionType, wasReflectionWrapper: false));
        Assert.False(notWrapped.TargetException!.WasReflectionWrapper);

        var wrapped = DeserializeTargetFailure(
            TargetFailedResponse(exceptionType, wasReflectionWrapper: true));
        Assert.True(wrapped.TargetException!.WasReflectionWrapper);

        var serialized = ClrInspectionJsonCodec.SerializeReadPropertyValueResponse(
            new ReadPropertyValueResponseDto(
                new MemberReadResultDto(
                    new MemberRefDto("session", "member", "boundary", "type"),
                    ClrReadOutcome.TargetFailed,
                    targetException: new TargetExceptionDto(
                        new TypeIdentityDto(
                            "exception",
                            "Example.Exception",
                            "assembly",
                            "boundary",
                            isValueType: false,
                            Array.Empty<TypeRefDto>(),
                            Array.Empty<TypeRefDto>()),
                        wasReflectionWrapper: false))));
        Assert.Contains("\"wasReflectionWrapper\":false", serialized.GetRawText(), StringComparison.Ordinal);

        using var missing = JsonDocument.Parse(
            TargetFailedResponse(exceptionType, wasReflectionWrapper: null));
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadPropertyValueResponse(missing.RootElement));
    }

    [Fact]
    public void Value_type_sparse_boolean_flags_omit_false_but_decode_explicit_true()
    {
        var omitted = RemoveProperty(
            RemoveProperty(
                ValueTypeJson("Root", string.Empty),
                "structTruncated"),
            "structNotExpanded");
        var omittedDecoded = DecodeValue(omitted);
        Assert.False(omittedDecoded.StructTruncated);
        Assert.False(omittedDecoded.StructNotExpanded);

        var explicitNotExpanded = DecodeValue(
            ValueTypeJson("Root", string.Empty, notExpanded: true));
        Assert.True(explicitNotExpanded.StructNotExpanded);

        var explicitTruncated = DecodeValue(
            ValueTypeJson(
                "Root",
                StructFieldJson("Number", "{\"kind\":\"Null\"}"),
                truncated: true));
        Assert.True(explicitTruncated.StructTruncated);
    }

    [Fact]
    public void Nested_wire_objects_with_missing_required_properties_are_protocol_errors()
    {
        var malformedManagedObject = "{\"handle\":null,\"boundaryId\":\"boundary\",\"contextId\":\"context\"}";
        AssertInvalidValue(
            $"{{\"kind\":\"ObjectReference\",\"objectType\":{TypeIdentityJson("Object", isValueType: false)},\"objectReference\":{malformedManagedObject}}}");
        AssertInvalidValue(
            $"{{\"kind\":\"TypeObject\",\"representedType\":{TypeIdentityJson("Represented", isValueType: false)},\"typeObjectReference\":{malformedManagedObject}}}");

        var malformedType = TypeIdentityJson("Malformed", isValueType: true)
            .Replace("\"fullName\":\"Malformed\"", "\"fullName\":null", StringComparison.Ordinal);
        AssertInvalidValue(
            $"{{\"kind\":\"Enum\",\"enumType\":{malformedType},\"enumUnderlyingKind\":\"Int32\",\"enumUnderlyingValue\":\"1\"}}");
        AssertInvalidValue(ValueTypeJson("Root", string.Empty, typeFullName: null)
            .Replace("\"fullName\":\"Root\"", "\"fullName\":null", StringComparison.Ordinal));

        using var malformedMember = JsonDocument.Parse(
            "{\"members\":[{\"member\":null,\"kind\":\"Field\",\"name\":\"Field\",\"valueType\":{\"typeId\":\"int\",\"boundaryId\":\"boundary\"}}]}");
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeListMembersResponse(malformedMember.RootElement));

        using var malformedException = JsonDocument.Parse(
            "{\"result\":{\"member\":{\"sessionId\":\"session\",\"memberId\":\"member\",\"boundaryId\":\"boundary\",\"declaringTypeId\":\"type\"},\"outcome\":\"TargetFailed\",\"targetException\":{\"exceptionType\":null,\"wasReflectionWrapper\":false}}}");
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadPropertyValueResponse(malformedException.RootElement));
    }

    [Fact]
    public void Unknown_and_cross_branch_properties_are_rejected()
    {
        var member = new MemberRefDto("session", "member", "boundary", "type");
        var response = new ReadFieldValuesResponseDto(new[]
        {
            new MemberReadResultDto(member, ClrReadOutcome.Available, ClrValueDto.Null())
        });
        var payload = ClrInspectionJsonCodec.SerializeReadFieldValuesResponse(response);
        var node = JsonNode.Parse(payload.GetRawText())!.AsObject();
        node["results"]![0]!["value"]!["integerValue"] = "1";

        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(
                JsonDocument.Parse(node.ToJsonString()).RootElement));
    }

    [Fact]
    public void Duplicate_nested_properties_are_rejected_before_deserialization()
    {
        const string payload = """
            {
              "results": [
                {
                  "member": {
                    "sessionId": "session",
                    "memberId": "member",
                    "boundaryId": "boundary",
                    "declaringTypeId": "type"
                  },
                  "outcome": "Available",
                  "value": { "kind": "Null", "kind": "Null" }
                }
              ]
            }
            """;

        using var document = JsonDocument.Parse(payload);
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(document.RootElement));
    }

    private static ClrValueDto DecodeValue(string valueJson)
    {
        using var document = JsonDocument.Parse(ResponseJson(valueJson));
        var response = ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(document.RootElement);
        return response.Results[0].Value!;
    }

    private static void AssertInvalidValue(string valueJson)
    {
        using var document = JsonDocument.Parse(ResponseJson(valueJson));
        Assert.Throws<ProtocolJsonException>(() =>
            ClrInspectionJsonCodec.DeserializeReadFieldValuesResponse(document.RootElement));
    }

    private static string ResponseJson(string valueJson)
    {
        return "{\"results\":[{\"member\":{\"sessionId\":\"session\",\"memberId\":\"member\",\"boundaryId\":\"boundary\",\"declaringTypeId\":\"type\"},\"outcome\":\"Available\",\"value\":"
            + valueJson
            + "}]}";
    }

    private static string EnumJson(string underlyingKind, string underlyingValue)
    {
        return EnumJsonWithType(
            TypeIdentityJson("Enum", isValueType: true),
            underlyingKind,
            underlyingValue);
    }

    private static string EnumJsonWithType(
        string typeJson,
        string underlyingKind = "Int32",
        string underlyingValue = "1")
    {
        return $"{{\"kind\":\"Enum\",\"enumType\":{typeJson},\"enumUnderlyingKind\":\"{underlyingKind}\",\"enumUnderlyingValue\":\"{underlyingValue}\"}}";
    }

    private static string ObjectReferenceValueJson(string typeJson)
    {
        return $"{{\"kind\":\"ObjectReference\",\"objectType\":{typeJson},\"objectReference\":{ManagedObjectJson()}}}";
    }

    private static string TypeObjectValueJson(string typeJson)
    {
        return $"{{\"kind\":\"TypeObject\",\"representedType\":{typeJson}}}";
    }

    private static string DescribeResponseJson(string typeJson)
    {
        return $"{{\"object\":{{\"handle\":{{\"sessionId\":\"session\",\"handleId\":\"object\",\"generation\":1,\"kind\":\"ClrObject\",\"boundaryId\":\"boundary\"}},\"typeIdentity\":{typeJson},\"boundaryId\":\"boundary\"}}}}";
    }

    private static string RemoveEmptyArrayProperty(string json, string propertyName)
    {
        return json.Replace($",\"{propertyName}\":[]", string.Empty, StringComparison.Ordinal);
    }

    private static string ReplaceEmptyArrayWithNull(string json, string propertyName)
    {
        return json.Replace($"\"{propertyName}\":[]", $"\"{propertyName}\":null", StringComparison.Ordinal);
    }

    private static string ReplaceEmptyArrayWithNullElement(string json, string propertyName)
    {
        return json.Replace($"\"{propertyName}\":[]", $"\"{propertyName}\":[null]", StringComparison.Ordinal);
    }

    private static string RemoveProperty(string json, string propertyName)
    {
        return json.Replace($",\"{propertyName}\":false", string.Empty, StringComparison.Ordinal);
    }

    private static MemberReadResultDto DeserializeTargetFailure(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        return ClrInspectionJsonCodec.DeserializeReadPropertyValueResponse(document.RootElement).Result;
    }

    private static string TargetFailedResponse(string exceptionTypeJson, bool? wasReflectionWrapper)
    {
        var wrapper = wasReflectionWrapper is bool value
            ? $",\"wasReflectionWrapper\":{value.ToString().ToLowerInvariant()}"
            : string.Empty;
        return $"{{\"result\":{{\"member\":{{\"sessionId\":\"session\",\"memberId\":\"member\",\"boundaryId\":\"boundary\",\"declaringTypeId\":\"type\"}},\"outcome\":\"TargetFailed\",\"targetException\":{{\"exceptionType\":{exceptionTypeJson}{wrapper}}}}}}}";
    }

    private static string ValueTypeJson(
        string typeId,
        string fields,
        bool truncated = false,
        bool notExpanded = false,
        string? typeFullName = null)
    {
        return $"{{\"kind\":\"ValueType\",\"valueType\":{TypeIdentityJson(typeId, isValueType: true, fullName: typeFullName)},\"structFields\":[{fields}],\"structTruncated\":{truncated.ToString().ToLowerInvariant()},\"structNotExpanded\":{notExpanded.ToString().ToLowerInvariant()}}}";
    }

    private static string StructFieldJson(string name, string value)
    {
        return $"{{\"name\":\"{name}\",\"fieldType\":{{\"typeId\":\"field-type\",\"boundaryId\":\"boundary\"}},\"outcome\":\"Available\",\"value\":{value}}}";
    }

    private static string TypeIdentityJson(string typeId, bool isValueType, string? fullName = null)
    {
        return $"{{\"typeId\":\"{typeId}\",\"fullName\":\"{fullName ?? typeId}\",\"assemblySimpleName\":\"assembly\",\"boundaryId\":\"boundary\",\"isValueType\":{isValueType.ToString().ToLowerInvariant()},\"genericArguments\":[],\"interfaces\":[]}}";
    }

    private static string ManagedObjectJson()
    {
        return "{\"handle\":{\"sessionId\":\"session\",\"handleId\":\"object\",\"generation\":1,\"kind\":\"ClrObject\",\"boundaryId\":\"boundary\"},\"boundaryId\":\"boundary\"}";
    }
}
