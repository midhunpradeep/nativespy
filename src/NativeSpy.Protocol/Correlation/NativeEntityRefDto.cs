using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class NativeEntityRefDto
{
    public NativeEntityRefDto(
        NativeBoundaryKind boundaryKind,
        HwndInfoDto? hwndObservation = null,
        int? processId = null,
        long? providerObservationEpoch = null,
        IEnumerable<GenerationRefDto>? generationRefs = null,
        AdapterMetadataDto? adapterMetadata = null)
    {
        BoundaryKind = boundaryKind;
        if (processId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "Process ID must be positive when supplied.");
        }

        if (providerObservationEpoch is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerObservationEpoch),
                providerObservationEpoch,
                "Provider observation epoch must be positive when supplied.");
        }

        HwndObservation = hwndObservation;
        ProcessId = processId;
        ProviderObservationEpoch = providerObservationEpoch;
        GenerationRefs = generationRefs is null
            ? Array.Empty<GenerationRefDto>()
            : ContractValidation.CopyRequired(generationRefs, nameof(generationRefs));
        AdapterMetadata = adapterMetadata;
    }

    public NativeBoundaryKind BoundaryKind { get; }

    public HwndInfoDto? HwndObservation { get; }

    public int? ProcessId { get; }

    public long? ProviderObservationEpoch { get; }

    public IReadOnlyList<GenerationRefDto> GenerationRefs { get; }

    public AdapterMetadataDto? AdapterMetadata { get; }
}
