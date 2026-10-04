using System.Globalization;
using System.Reflection;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent;

internal sealed class ClrValueConversionResult
{
    private ClrValueConversionResult(
        ClrReadOutcome outcome,
        ClrValueDto? value,
        OperationErrorCode? errorCode)
    {
        Outcome = outcome;
        Value = value;
        ErrorCode = errorCode;
    }

    public ClrReadOutcome Outcome { get; }

    public ClrValueDto? Value { get; }

    public OperationErrorCode? ErrorCode { get; }

    public static ClrValueConversionResult Available(ClrValueDto value)
    {
        return new ClrValueConversionResult(
            ClrReadOutcome.Available,
            value ?? throw new ArgumentNullException(nameof(value)),
            null);
    }

    public static ClrValueConversionResult Unsupported()
    {
        return new ClrValueConversionResult(ClrReadOutcome.Unsupported, null, null);
    }

    public static ClrValueConversionResult Unavailable(OperationErrorCode code)
    {
        return new ClrValueConversionResult(ClrReadOutcome.Unavailable, null, code);
    }
}

internal sealed class ClrValueConverter
{
    private readonly ClrAgentSession _session;
    private int _newObjectReferences;

    public ClrValueConverter(ClrAgentSession session)
    {
        _session = session;
    }

    public ClrValueConversionResult Convert(object? value, bool expandValueType = true)
    {
        if (value is null)
        {
            return ClrValueConversionResult.Available(ClrValueDto.Null());
        }

        var type = value.GetType();
        try
        {
            if (value is bool boolean)
            {
                return ClrValueConversionResult.Available(ClrValueDto.Boolean(boolean));
            }

            if (type.IsEnum)
            {
                return ConvertEnum(value, type);
            }

            if (TryGetIntegerKind(type, out var integerKind))
            {
                return ClrValueConversionResult.Available(
                    ClrValueDto.Integer(integerKind, FormatInteger(value, integerKind)));
            }

            if (value is float single)
            {
                return ClrValueConversionResult.Available(
                    ClrValueDto.FloatingPoint(
                        ClrFloatingPointKind.Single,
                        BitConverter.SingleToInt32Bits(single).ToString("X8", CultureInfo.InvariantCulture),
                        single.ToString("R", CultureInfo.InvariantCulture)));
            }

            if (value is double doubleValue)
            {
                return ClrValueConversionResult.Available(
                    ClrValueDto.FloatingPoint(
                        ClrFloatingPointKind.Double,
                        BitConverter.DoubleToInt64Bits(doubleValue).ToString("X16", CultureInfo.InvariantCulture),
                        doubleValue.ToString("R", CultureInfo.InvariantCulture)));
            }

            if (value is decimal decimalValue)
            {
                return ClrValueConversionResult.Available(
                    ClrValueDto.Decimal(
                        decimal.GetBits(decimalValue),
                        decimalValue.ToString(CultureInfo.InvariantCulture)));
            }

            if (value is char character)
            {
                return ClrValueConversionResult.Available(ClrValueDto.Char(character));
            }

            if (value is string text)
            {
                return ConvertString(text);
            }

            if (value is Guid guid)
            {
                return ClrValueConversionResult.Available(ClrValueDto.Guid(guid));
            }

            if (value is DateTime dateTime)
            {
                return ClrValueConversionResult.Available(
                    ClrValueDto.DateTimeValue(dateTime.Ticks, dateTime.Kind));
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                return ClrValueConversionResult.Available(
                    ClrValueDto.DateTimeOffsetValue(
                        dateTimeOffset.Ticks,
                        checked((short)dateTimeOffset.Offset.TotalMinutes)));
            }

            if (value is TimeSpan timeSpan)
            {
                return ClrValueConversionResult.Available(ClrValueDto.TimeSpanValue(timeSpan.Ticks));
            }

            if (value is Type representedType)
            {
                if (!TryGetTypeIdentity(representedType, out var representedIdentity))
                {
                    return ClrValueConversionResult.Unavailable(OperationErrorCode.RuntimeUnavailable);
                }

                ManagedObjectRefDto? typeObjectReference = null;
                if (_newObjectReferences < ClrInspectionLimits.MaxNewObjectReferencesPerResponse)
                {
                    _newObjectReferences++;
                    var registration = _session.Register(value);
                    if (registration.IsSuccess)
                    {
                        typeObjectReference = registration.Reference;
                    }
                }

                return ClrValueConversionResult.Available(
                    ClrValueDto.TypeObject(representedIdentity!, typeObjectReference));
            }

            if (type.IsValueType)
            {
                return ConvertValueType(value, type, expandValueType);
            }

            return ConvertObjectReference(value, type);
        }
        catch (Exception exception) when (IsConversionFailure(exception))
        {
            return ClrValueConversionResult.Unavailable(OperationErrorCode.MemberUnavailable);
        }
    }

    private ClrValueConversionResult ConvertString(string value)
    {
        var originalLength = value.Length;
        var returnedLength = Math.Min(originalLength, ClrInspectionLimits.MaxStringCodeUnits);
        if (returnedLength < originalLength
            && returnedLength > 0
            && char.IsHighSurrogate(value[returnedLength - 1]))
        {
            returnedLength--;
        }

        var returned = returnedLength == originalLength
            ? value
            : value.Substring(0, returnedLength);
        return ClrValueConversionResult.Available(
            ClrValueDto.String(
                returned,
                originalLength,
                returnedLength,
                returnedLength < originalLength));
    }

    private ClrValueConversionResult ConvertEnum(object value, Type type)
    {
        if (!TryGetIntegerKind(Enum.GetUnderlyingType(type), out var underlyingKind))
        {
            return ClrValueConversionResult.Unsupported();
        }

        if (!TryGetTypeIdentity(type, out var enumIdentity))
        {
            return ClrValueConversionResult.Unavailable(OperationErrorCode.RuntimeUnavailable);
        }

        var underlyingValue = FormatInteger(value, underlyingKind);
        var name = Enum.GetName(type, value);
        return ClrValueConversionResult.Available(
            ClrValueDto.EnumValue(enumIdentity!, underlyingKind, underlyingValue, name));
    }

    private ClrValueConversionResult ConvertValueType(object value, Type type, bool expandValueType)
    {
        if (!TryGetTypeIdentity(type, out var identity))
        {
            return ClrValueConversionResult.Unavailable(OperationErrorCode.RuntimeUnavailable);
        }

        if (!expandValueType)
        {
            return ClrValueConversionResult.Available(
                ClrValueDto.CreateValueType(identity!, Array.Empty<ClrStructFieldDto>(), false, true));
        }

        var fields = type
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();
        var truncated = fields.Length > ClrInspectionLimits.MaxStructFields;
        var entries = new List<ClrStructFieldDto>();
        var estimatedBytes = 0;
        var fieldCount = Math.Min(fields.Length, ClrInspectionLimits.MaxStructFields);
        for (var index = 0; index < fieldCount; index++)
        {
            var field = fields[index];
            if (!TryGetTypeIdentity(field.FieldType, out var fieldTypeIdentity))
            {
                entries.Add(new ClrStructFieldDto(
                    field.Name,
                    new TypeRefDto(identity!.TypeId, identity.BoundaryId),
                    ClrStructFieldOutcome.Unavailable));
                estimatedBytes = checked(estimatedBytes + field.Name.Length * 2 + 64);
                continue;
            }

            ClrValueConversionResult conversion;
            try
            {
                conversion = Convert(field.GetValue(value), expandValueType: false);
            }
            catch (Exception exception) when (IsConversionFailure(exception))
            {
                conversion = ClrValueConversionResult.Unavailable(OperationErrorCode.MemberUnavailable);
            }

            var fieldEstimate = checked(
                field.Name.Length * 2
                + fieldTypeIdentity!.TypeId.Length * 2
                + EstimateValuePayload(conversion.Value));
            if (estimatedBytes + fieldEstimate > ClrInspectionLimits.MaxStructPayloadBytes)
            {
                truncated = true;
                break;
            }

            estimatedBytes += fieldEstimate;
            entries.Add(new ClrStructFieldDto(
                field.Name,
                new TypeRefDto(fieldTypeIdentity.TypeId, fieldTypeIdentity.BoundaryId),
                conversion.Outcome switch
                {
                    ClrReadOutcome.Available => ClrStructFieldOutcome.Available,
                    ClrReadOutcome.Unsupported => ClrStructFieldOutcome.Unsupported,
                    _ => ClrStructFieldOutcome.Unavailable
                },
                conversion.Value));
        }

        return ClrValueConversionResult.Available(
            ClrValueDto.CreateValueType(identity!, entries, truncated, false));
    }

    private ClrValueConversionResult ConvertObjectReference(object value, Type type)
    {
        if (_newObjectReferences >= ClrInspectionLimits.MaxNewObjectReferencesPerResponse)
        {
            return ClrValueConversionResult.Unavailable(OperationErrorCode.RegistryQuotaExceeded);
        }

        if (!TryGetTypeIdentity(type, out var objectIdentity))
        {
            return ClrValueConversionResult.Unavailable(OperationErrorCode.RuntimeUnavailable);
        }

        _newObjectReferences++;
        var registration = _session.Register(value);
        if (!registration.IsSuccess)
        {
            return ClrValueConversionResult.Unavailable(
                registration.Error?.Code ?? OperationErrorCode.RegistryQuotaExceeded);
        }

        return ClrValueConversionResult.Available(
            ClrValueDto.CreateObjectReference(registration.Reference!, objectIdentity!));
    }

    private bool TryGetTypeIdentity(Type type, out TypeIdentityDto? identity)
    {
        return _session.TryGetOrCreateTypeIdentity(type, out identity);
    }

    private static int EstimateValuePayload(ClrValueDto? value)
    {
        if (value is null)
        {
            return 32;
        }

        return value.Kind switch
        {
            ClrValueKind.Null => 32,
            ClrValueKind.Boolean => 32,
            ClrValueKind.Integer => 64 + (value.IntegerValue?.Length ?? 0) * 2,
            ClrValueKind.FloatingPoint => 96 + (value.FloatingPointBits?.Length ?? 0) * 2 + (value.FloatingPointDisplay?.Length ?? 0) * 2,
            ClrValueKind.Decimal => 128 + (value.DecimalDisplay?.Length ?? 0) * 2,
            ClrValueKind.Char => 48,
            ClrValueKind.String => 96 + (value.StringValue?.Length ?? 0) * 2,
            ClrValueKind.Guid => 96,
            ClrValueKind.DateTime => 64,
            ClrValueKind.DateTimeOffset => 80,
            ClrValueKind.TimeSpan => 64,
            ClrValueKind.Enum => 128 + (value.EnumUnderlyingValue?.Length ?? 0) * 2 + (value.EnumName?.Length ?? 0) * 2,
            ClrValueKind.TypeObject => 192,
            ClrValueKind.ObjectReference => 192,
            ClrValueKind.ValueType => 128 + (value.StructFields?.Sum(field => EstimateValuePayload(field.Value) + field.Name.Length * 2) ?? 0),
            _ => 256
        };
    }

    private static bool TryGetIntegerKind(Type type, out ClrIntegerKind kind)
    {
        kind = Type.GetTypeCode(type) switch
        {
            TypeCode.SByte => ClrIntegerKind.SByte,
            TypeCode.Byte => ClrIntegerKind.Byte,
            TypeCode.Int16 => ClrIntegerKind.Int16,
            TypeCode.UInt16 => ClrIntegerKind.UInt16,
            TypeCode.Int32 => ClrIntegerKind.Int32,
            TypeCode.UInt32 => ClrIntegerKind.UInt32,
            TypeCode.Int64 => ClrIntegerKind.Int64,
            TypeCode.UInt64 => ClrIntegerKind.UInt64,
            _ => default
        };
        return Type.GetTypeCode(type) is TypeCode.SByte
            or TypeCode.Byte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64;
    }

    private static string FormatInteger(object value, ClrIntegerKind kind)
    {
        return kind switch
        {
            ClrIntegerKind.SByte => System.Convert.ToSByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ClrIntegerKind.Byte => System.Convert.ToByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ClrIntegerKind.Int16 => System.Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ClrIntegerKind.UInt16 => System.Convert.ToUInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ClrIntegerKind.Int32 => System.Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ClrIntegerKind.UInt32 => System.Convert.ToUInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ClrIntegerKind.Int64 => System.Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ClrIntegerKind.UInt64 => System.Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("The integer kind is not supported.")
        };
    }

    private static bool IsConversionFailure(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or FieldAccessException
            or TargetException
            or OverflowException;
    }
}
