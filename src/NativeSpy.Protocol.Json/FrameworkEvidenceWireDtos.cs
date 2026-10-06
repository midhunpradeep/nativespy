namespace NativeSpy.Protocol.Json;

internal sealed class FrameworkEvidenceWire
{
    public string AdapterId { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public TargetWire? CandidateTarget { get; set; }
    public EvidenceFactWire[]? EvidenceFacts { get; set; }
    public ValidationFactWire[]? ValidationFacts { get; set; }
    public EffectWire? Effects { get; set; }
    public MetadataWire[]? AdapterMetadata { get; set; }
    public LimitationWire[]? Limitations { get; set; }
    public OperationErrorWire? OperationError { get; set; }
}

internal sealed class TargetWire
{
    public string TargetKind { get; set; } = string.Empty;
    public ManagedObjectWire? Managed { get; set; }
    public FrameworkEntityWire? Framework { get; set; }
    public NativeEntityWire? Native { get; set; }
}

internal sealed class ManagedObjectWire
{
    public HandleWire? Handle { get; set; }
    public TypeIdentityWire? TypeIdentity { get; set; }
    public string? BoundaryId { get; set; }
    public string? ContextId { get; set; }
}

internal sealed class FrameworkEntityWire
{
    public string AdapterId { get; set; } = string.Empty;
    public string EntityKind { get; set; } = string.Empty;
    public HandleWire? LiveHandle { get; set; }
    public MetadataValueWire? Locator { get; set; }
    public GenerationWire[]? GenerationRefs { get; set; }
    public MetadataWire? AdapterMetadata { get; set; }
}

internal sealed class NativeEntityWire
{
    public string BoundaryKind { get; set; } = string.Empty;
    public HwndWire? HwndObservation { get; set; }
    public int? ProcessId { get; set; }
    public long? ProviderObservationEpoch { get; set; }
    public GenerationWire[]? GenerationRefs { get; set; }
    public MetadataWire? AdapterMetadata { get; set; }
}

internal sealed class HandleWire
{
    public string SessionId { get; set; } = string.Empty;
    public string HandleId { get; set; } = string.Empty;
    public long Generation { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? BoundaryId { get; set; }
}

internal sealed class TypeIdentityWire
{
    public string TypeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string AssemblySimpleName { get; set; } = string.Empty;
    public string? AssemblyVersion { get; set; }
    public string? AssemblyCulture { get; set; }
    public string? PublicKeyToken { get; set; }
    public string? ModuleVersionId { get; set; }
    public string BoundaryId { get; set; } = string.Empty;
    public TypeRefWire? DeclaringType { get; set; }
    public TypeRefWire? GenericDefinition { get; set; }
    public TypeRefWire[]? GenericArguments { get; set; }
    public int? ArrayRank { get; set; }
    public int[]? ArrayShape { get; set; }
    public TypeRefWire? PointerElementType { get; set; }
    public TypeRefWire? ByRefElementType { get; set; }
    public TypeRefWire? NullableUnderlyingType { get; set; }
    // Required type identity data: nullable preserves wire presence so an omitted
    // false value cannot be confused with an explicit false.
    public bool? IsValueType { get; set; }
    public TypeRefWire? BaseType { get; set; }
    public TypeRefWire[]? Interfaces { get; set; }
    public string? DynamicIdentity { get; set; }
}

internal sealed class TypeRefWire
{
    public string TypeId { get; set; } = string.Empty;
    public string BoundaryId { get; set; } = string.Empty;
}

internal sealed class HwndWire
{
    public ulong Hwnd { get; set; }
    public string HwndGeneration { get; set; } = string.Empty;
}

internal sealed class GenerationWire
{
    public string AdapterId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? ScopeId { get; set; }
}

internal sealed class EvidenceFactWire
{
    public string Name { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string EvidenceKind { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

internal sealed class ValidationFactWire
{
    public string Name { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

internal sealed class EffectWire
{
    public string[]? Categories { get; set; }
    public string FrameworkState { get; set; } = string.Empty;
    public string ApplicationCallbacks { get; set; } = string.Empty;
    public CallbackWire[]? CallbackDetails { get; set; }
    public string VisibleMutation { get; set; } = string.Empty;
    public string[]? Operations { get; set; }
}

internal sealed class CallbackWire
{
    public string Name { get; set; } = string.Empty;
    public int? Count { get; set; }
    // Optional-by-design: absence preserves CallbackDetailDto's unknown-count
    // default (false); a supplied Count still has independent DTO validation.
    public bool CountKnown { get; set; }
}

internal sealed class MetadataWire
{
    public string AdapterId { get; set; } = string.Empty;
    public string SchemaId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public MetadataValueWire? Payload { get; set; }
}

internal sealed class MetadataValueWire
{
    public string Kind { get; set; } = string.Empty;
    public bool? BooleanValue { get; set; }
    public long? IntegerValue { get; set; }
    public decimal? DecimalValue { get; set; }
    public double? FloatingPointValue { get; set; }
    public string? StringValue { get; set; }
    public MetadataValueWire[]? ArrayValue { get; set; }
    public MetadataPropertyWire[]? ObjectValue { get; set; }
}

internal sealed class MetadataPropertyWire
{
    public string Name { get; set; } = string.Empty;
    public MetadataValueWire Value { get; set; } = new();
}

internal sealed class LimitationWire
{
    public string Code { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
