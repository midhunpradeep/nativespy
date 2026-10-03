using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class PositiveNoDirectMappingAssessment
{
    public PositiveNoDirectMappingAssessment(
        bool isEstablished,
        RelationshipKind? relationshipKind,
        RelationshipScope scope)
    {
        IsEstablished = isEstablished;
        RelationshipKind = relationshipKind;
        Scope = scope;
    }

    public bool IsEstablished { get; }

    public RelationshipKind? RelationshipKind { get; }

    public RelationshipScope Scope { get; }
}
