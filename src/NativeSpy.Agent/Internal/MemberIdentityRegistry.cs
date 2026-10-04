using System.Reflection;
using NativeSpy.Protocol.Clr;

namespace NativeSpy.Agent.Internal;

internal sealed class MemberStructuralKey : IEquatable<MemberStructuralKey>
{
    public MemberStructuralKey(
        ClrMemberKind kind,
        string boundaryId,
        string declaringTypeId,
        string name,
        string valueTypeId,
        IEnumerable<string> indexParameterTypeIds,
        string accessorShape,
        bool isStatic,
        string? moduleVersionId,
        int? metadataToken)
    {
        Kind = kind;
        BoundaryId = boundaryId;
        DeclaringTypeId = declaringTypeId;
        Name = name;
        ValueTypeId = valueTypeId;
        IndexParameterTypeIds = indexParameterTypeIds.ToArray();
        AccessorShape = accessorShape;
        IsStatic = isStatic;
        ModuleVersionId = moduleVersionId;
        MetadataToken = metadataToken;
    }

    public ClrMemberKind Kind { get; }

    public string BoundaryId { get; }

    public string DeclaringTypeId { get; }

    public string Name { get; }

    public string ValueTypeId { get; }

    public IReadOnlyList<string> IndexParameterTypeIds { get; }

    public string AccessorShape { get; }

    public bool IsStatic { get; }

    public string? ModuleVersionId { get; }

    public int? MetadataToken { get; }

    public bool Equals(MemberStructuralKey? other)
    {
        if (other is null
            || Kind != other.Kind
            || IsStatic != other.IsStatic
            || !string.Equals(BoundaryId, other.BoundaryId, StringComparison.Ordinal)
            || !string.Equals(DeclaringTypeId, other.DeclaringTypeId, StringComparison.Ordinal)
            || !string.Equals(Name, other.Name, StringComparison.Ordinal)
            || !string.Equals(ValueTypeId, other.ValueTypeId, StringComparison.Ordinal)
            || !string.Equals(AccessorShape, other.AccessorShape, StringComparison.Ordinal)
            || !string.Equals(ModuleVersionId, other.ModuleVersionId, StringComparison.Ordinal)
            || MetadataToken != other.MetadataToken
            || IndexParameterTypeIds.Count != other.IndexParameterTypeIds.Count)
        {
            return false;
        }

        for (var index = 0; index < IndexParameterTypeIds.Count; index++)
        {
            if (!string.Equals(IndexParameterTypeIds[index], other.IndexParameterTypeIds[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MemberStructuralKey);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(BoundaryId, StringComparer.Ordinal);
        hash.Add(DeclaringTypeId, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(ValueTypeId, StringComparer.Ordinal);
        hash.Add(AccessorShape, StringComparer.Ordinal);
        hash.Add(IsStatic);
        hash.Add(ModuleVersionId, StringComparer.Ordinal);
        hash.Add(MetadataToken);
        foreach (var parameterTypeId in IndexParameterTypeIds)
        {
            hash.Add(parameterTypeId, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public string CanonicalText()
    {
        return string.Join(
            "|",
            Kind,
            BoundaryId,
            DeclaringTypeId,
            Name,
            ValueTypeId,
            string.Join(",", IndexParameterTypeIds),
            AccessorShape,
            IsStatic ? "static" : "instance",
            ModuleVersionId ?? string.Empty,
            MetadataToken?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }
}

internal sealed class MemberRecord
{
    public MemberRecord(
        string memberId,
        MemberStructuralKey key,
        NativeSpy.Protocol.Clr.MemberDescriptorDto descriptor)
    {
        MemberId = memberId;
        Key = key;
        Descriptor = descriptor;
    }

    public string MemberId { get; }

    public MemberStructuralKey Key { get; }

    public NativeSpy.Protocol.Clr.MemberDescriptorDto Descriptor { get; }
}

internal sealed class MemberIdentityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<MemberStructuralKey, MemberRecord> _byKey = new();
    private readonly Dictionary<string, MemberRecord> _byId = new(StringComparer.Ordinal);
    private readonly string _sessionId;
    private long _nextMemberId;

    public MemberIdentityRegistry(string sessionId)
    {
        _sessionId = sessionId;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byId.Count;
            }
        }
    }

    public MemberRecord GetOrAdd(
        MemberStructuralKey key,
        Func<string, NativeSpy.Protocol.Clr.MemberDescriptorDto> descriptorFactory)
    {
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var existing))
            {
                return existing;
            }

            if (_byId.Count >= ClrInspectionLimits.MaxMemberRecordsPerSession)
            {
                throw new ClrInspectionQuotaException(
                    "The Agent session exhausted its member identity quota.");
            }

            if (_nextMemberId == long.MaxValue)
            {
                throw new ClrInspectionQuotaException(
                    "The Agent session exhausted its member identity allocation space.");
            }

            var memberId = $"clr-member-{++_nextMemberId}";
            var descriptor = descriptorFactory(memberId)
                ?? throw new ArgumentNullException(nameof(descriptorFactory));
            var record = new MemberRecord(memberId, key, descriptor);
            _byKey.Add(key, record);
            _byId.Add(memberId, record);
            return record;
        }
    }

    public bool TryGet(string memberId, out MemberRecord? record)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(memberId, out record);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _byKey.Clear();
            _byId.Clear();
        }
    }
}

internal sealed class ClrInspectionQuotaException : Exception
{
    public ClrInspectionQuotaException(string message)
        : base(message)
    {
    }
}
