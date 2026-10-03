using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class FrameworkEntityRefDto
{
    public FrameworkEntityRefDto(
        string adapterId,
        FrameworkEntityKind entityKind,
        HandleRefDto? liveHandle = null,
        DetachedMetadataValueDto? locator = null,
        IEnumerable<GenerationRefDto>? generationRefs = null,
        AdapterMetadataDto? adapterMetadata = null)
    {
        AdapterId = ContractValidation.RequiredIdentifier(adapterId, nameof(adapterId));
        EntityKind = entityKind;
        if (liveHandle is not null && liveHandle.Kind != HandleKind.AgentEntity)
        {
            throw new ArgumentException("A framework entity live handle must be an AgentEntity handle.", nameof(liveHandle));
        }

        LiveHandle = liveHandle;
        Locator = locator;
        GenerationRefs = generationRefs is null
            ? Array.Empty<GenerationRefDto>()
            : ContractValidation.CopyRequired(generationRefs, nameof(generationRefs));
        AdapterMetadata = adapterMetadata;
    }

    public string AdapterId { get; }

    public FrameworkEntityKind EntityKind { get; }

    public HandleRefDto? LiveHandle { get; }

    public DetachedMetadataValueDto? Locator { get; }

    public IReadOnlyList<GenerationRefDto> GenerationRefs { get; }

    public AdapterMetadataDto? AdapterMetadata { get; }
}
