using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class DetachedMetadataPropertyDto
{
    public DetachedMetadataPropertyDto(string name, DetachedMetadataValueDto value)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Name { get; }

    public DetachedMetadataValueDto Value { get; }
}
