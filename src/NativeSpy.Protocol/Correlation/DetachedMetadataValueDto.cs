using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class DetachedMetadataValueDto
{
    private DetachedMetadataValueDto(
        DetachedMetadataValueKind kind,
        bool? booleanValue = null,
        long? integerValue = null,
        decimal? decimalValue = null,
        double? floatingPointValue = null,
        string? stringValue = null,
        IReadOnlyList<DetachedMetadataValueDto>? arrayValue = null,
        IReadOnlyList<DetachedMetadataPropertyDto>? objectValue = null)
    {
        Kind = kind;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        DecimalValue = decimalValue;
        FloatingPointValue = floatingPointValue;
        StringValue = stringValue;
        ArrayValue = arrayValue;
        ObjectValue = objectValue;

        ValidatePayload();
    }

    public DetachedMetadataValueKind Kind { get; }

    public bool? BooleanValue { get; }

    public long? IntegerValue { get; }

    public decimal? DecimalValue { get; }

    public double? FloatingPointValue { get; }

    public string? StringValue { get; }

    public IReadOnlyList<DetachedMetadataValueDto>? ArrayValue { get; }

    public IReadOnlyList<DetachedMetadataPropertyDto>? ObjectValue { get; }

    public static DetachedMetadataValueDto Null() => new(DetachedMetadataValueKind.Null);

    public static DetachedMetadataValueDto Boolean(bool value) =>
        new(DetachedMetadataValueKind.Boolean, booleanValue: value);

    public static DetachedMetadataValueDto Integer(long value) =>
        new(DetachedMetadataValueKind.Integer, integerValue: value);

    public static DetachedMetadataValueDto Decimal(decimal value) =>
        new(DetachedMetadataValueKind.Decimal, decimalValue: value);

    public static DetachedMetadataValueDto FloatingPoint(double value) =>
        new(DetachedMetadataValueKind.FloatingPoint, floatingPointValue: value);

    public static DetachedMetadataValueDto String(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new(DetachedMetadataValueKind.String, stringValue: value);
    }

    public static DetachedMetadataValueDto Array(IEnumerable<DetachedMetadataValueDto> values)
    {
        var copy = ContractValidation.CopyRequired(values, nameof(values));
        return new(DetachedMetadataValueKind.Array, arrayValue: copy);
    }

    public static DetachedMetadataValueDto Object(IEnumerable<DetachedMetadataPropertyDto> properties)
    {
        var copy = ContractValidation.CopyRequired(properties, nameof(properties));
        ContractValidation.RequireUniqueIdentifiers(copy.Select(static property => property.Name), nameof(properties));
        return new(DetachedMetadataValueKind.Object, objectValue: copy);
    }

    private void ValidatePayload()
    {
        var populated = 0;
        populated += BooleanValue.HasValue ? 1 : 0;
        populated += IntegerValue.HasValue ? 1 : 0;
        populated += DecimalValue.HasValue ? 1 : 0;
        populated += FloatingPointValue.HasValue ? 1 : 0;
        populated += StringValue is not null ? 1 : 0;
        populated += ArrayValue is not null ? 1 : 0;
        populated += ObjectValue is not null ? 1 : 0;

        if (Kind == DetachedMetadataValueKind.Null)
        {
            if (populated != 0)
            {
                throw new ArgumentException("Null metadata values cannot contain a payload.");
            }

            return;
        }

        if (populated != 1)
        {
            throw new ArgumentException("A non-null metadata value must contain exactly one payload.");
        }

        var compatible = Kind switch
        {
            DetachedMetadataValueKind.Boolean => BooleanValue.HasValue,
            DetachedMetadataValueKind.Integer => IntegerValue.HasValue,
            DetachedMetadataValueKind.Decimal => DecimalValue.HasValue,
            DetachedMetadataValueKind.FloatingPoint => FloatingPointValue.HasValue,
            DetachedMetadataValueKind.String => StringValue is not null,
            DetachedMetadataValueKind.Array => ArrayValue is not null,
            DetachedMetadataValueKind.Object => ObjectValue is not null,
            _ => false
        };

        if (!compatible)
        {
            throw new ArgumentException("The metadata payload must match its kind.");
        }
    }
}
