using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class AdapterMetadataDto
{
    public AdapterMetadataDto(
        string adapterId,
        string schemaId,
        int schemaVersion,
        DetachedMetadataValueDto payload)
    {
        AdapterId = ContractValidation.RequiredIdentifier(adapterId, nameof(adapterId));
        SchemaId = ContractValidation.RequiredIdentifier(schemaId, nameof(schemaId));
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Schema version must be positive.");
        }

        SchemaVersion = schemaVersion;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public string AdapterId { get; }

    public string SchemaId { get; }

    public int SchemaVersion { get; }

    public DetachedMetadataValueDto Payload { get; }
}
