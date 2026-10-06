using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NativeSpy.Agent.Internal;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent;

public sealed class ClrInspectionService : IDisposable
{
    private const string ContinuationVersion = "i4a-1";

    private readonly ClrAgentSession _session;
    private readonly MemberIdentityRegistry _memberRegistry;
    private readonly byte[] _continuationKey = RandomNumberGenerator.GetBytes(32);
    private int _disposed;

    public ClrInspectionService(ClrAgentSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _memberRegistry = new MemberIdentityRegistry(session.SessionId);
    }

    internal int MemberIdentityCount => _memberRegistry.Count;

    public ClrInspectionResult<DescribeObjectResponseDto> DescribeObject(
        DescribeObjectRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var acquisitionResult = _session.TryAcquire(request.Object.Handle);
        if (!acquisitionResult.IsSuccess)
        {
            return ClrInspectionResult<DescribeObjectResponseDto>.Failure(acquisitionResult.Error!);
        }

        using var acquisition = acquisitionResult.Acquisition!;
        var registration = _session.Register(acquisition.Target);
        if (!registration.IsSuccess)
        {
            return ClrInspectionResult<DescribeObjectResponseDto>.Failure(registration.Error!);
        }

        return ClrInspectionResult<DescribeObjectResponseDto>.Success(
            new DescribeObjectResponseDto(registration.Reference!));
    }

    public ClrInspectionResult<ListMembersResponseDto> ListMembers(
        ListMembersRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PageSize > ClrInspectionLimits.MaxMembersPerPage)
        {
            return Failure<ListMembersResponseDto>(
                OperationErrorCode.SerializationLimit,
                "The requested member page exceeds the maximum page size.");
        }

        var acquisitionResult = _session.TryAcquire(request.Object.Handle);
        if (!acquisitionResult.IsSuccess)
        {
            return ClrInspectionResult<ListMembersResponseDto>.Failure(acquisitionResult.Error!);
        }

        using var acquisition = acquisitionResult.Acquisition!;
        if (!TryBuildCandidates(
                acquisition.Target,
                acquisition.Handle.BoundaryId,
                request.Filter,
                out var candidateSet,
                out var buildError))
        {
            return ClrInspectionResult<ListMembersResponseDto>.Failure(buildError!);
        }

        var start = 0;
        if (request.ContinuationToken is not null)
        {
            if (!TryReadContinuation(
                    request.ContinuationToken,
                    acquisition.Handle.BoundaryId!,
                    candidateSet!.TypeIdentity!.TypeId,
                    request.Filter,
                    candidateSet.Fingerprint,
                    out start))
            {
                return Failure<ListMembersResponseDto>(
                    OperationErrorCode.InvalidContinuation,
                    "The continuation token does not match the current member set.");
            }
        }

        if (start < 0 || start > candidateSet!.Candidates.Count)
        {
            return Failure<ListMembersResponseDto>(
                OperationErrorCode.InvalidContinuation,
                "The continuation position is outside the current member set.");
        }

        var page = candidateSet.Candidates
            .Skip(start)
            .Take(request.PageSize)
            .ToArray();
        var descriptors = new List<MemberDescriptorDto>(page.Length);
        try
        {
            foreach (var candidate in page)
            {
                var record = _memberRegistry.GetOrAdd(
                    candidate.Key,
                    memberId => new MemberDescriptorDto(
                        new MemberRefDto(
                            _session.SessionId,
                            memberId,
                            candidate.Key.BoundaryId,
                            candidate.Key.DeclaringTypeId),
                        candidate.Key.Kind,
                        candidate.Name,
                        candidate.ValueType,
                        candidate.DisplaySignature));
                descriptors.Add(record.Descriptor);
            }
        }
        catch (ClrInspectionQuotaException exception)
        {
            return Failure<ListMembersResponseDto>(
                OperationErrorCode.RegistryQuotaExceeded,
                exception.Message);
        }

        var next = start + page.Length;
        string? continuation = null;
        if (next < candidateSet.Candidates.Count)
        {
            continuation = CreateContinuation(
                acquisition.Handle.BoundaryId!,
                candidateSet.TypeIdentity!.TypeId,
                request.Filter,
                candidateSet.Fingerprint,
                next);
            if (Encoding.UTF8.GetByteCount(continuation) > ClrInspectionLimits.MaxContinuationTokenBytes)
            {
                return Failure<ListMembersResponseDto>(
                    OperationErrorCode.SerializationLimit,
                    "The continuation token exceeded its maximum size.");
            }
        }

        return ClrInspectionResult<ListMembersResponseDto>.Success(
            new ListMembersResponseDto(descriptors, continuation));
    }

    public ClrInspectionResult<ReadFieldValuesResponseDto> ReadFieldValues(
        ReadFieldValuesRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Members.Count > ClrInspectionLimits.MaxFieldsPerBatch)
        {
            return Failure<ReadFieldValuesResponseDto>(
                OperationErrorCode.SerializationLimit,
                "The field batch exceeds the maximum field count.");
        }

        var acquisitionResult = _session.TryAcquire(request.Object.Handle);
        if (!acquisitionResult.IsSuccess)
        {
            return ClrInspectionResult<ReadFieldValuesResponseDto>.Failure(acquisitionResult.Error!);
        }

        using var acquisition = acquisitionResult.Acquisition!;
        var converter = new ClrValueConverter(_session);
        var results = new List<MemberReadResultDto>(request.Members.Count);
        foreach (var member in request.Members)
        {
            if (!TryResolveMember(
                    acquisition.Target,
                    acquisition.Handle.BoundaryId,
                    member,
                    out var candidate,
                    out var memberError))
            {
                results.Add(new MemberReadResultDto(
                    member,
                    ClrReadOutcome.Unavailable,
                    errorCode: memberError!.Code));
                continue;
            }

            if (candidate!.Field is null)
            {
                results.Add(new MemberReadResultDto(
                    member,
                    ClrReadOutcome.Unavailable,
                    errorCode: OperationErrorCode.InvalidMemberReference));
                continue;
            }

            try
            {
                var conversion = converter.Convert(candidate.Field.GetValue(acquisition.Target));
                results.Add(CreateReadResult(member, conversion));
            }
            catch (Exception exception) when (IsTargetFailure(exception))
            {
                var targetException = CreateTargetException(exception);
                if (targetException is null)
                {
                    return Failure<ReadFieldValuesResponseDto>(
                        OperationErrorCode.RuntimeUnavailable,
                        "The target exception type could not be described.");
                }

                results.Add(new MemberReadResultDto(
                    member,
                    ClrReadOutcome.TargetFailed,
                    targetException: targetException));
            }
        }

        return ClrInspectionResult<ReadFieldValuesResponseDto>.Success(
            new ReadFieldValuesResponseDto(results));
    }

    public async Task<ClrInspectionResult<ReadPropertyValueResponseDto>> ReadPropertyValueAsync(
        ReadPropertyValueRequestDto request,
        IClrInvocationScheduler scheduler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scheduler);

        var acquisitionResult = _session.TryAcquire(request.Object.Handle);
        if (!acquisitionResult.IsSuccess)
        {
            return ClrInspectionResult<ReadPropertyValueResponseDto>.Failure(acquisitionResult.Error!);
        }

        using var acquisition = acquisitionResult.Acquisition!;
        if (!TryResolveMember(
                acquisition.Target,
                acquisition.Handle.BoundaryId,
                request.Member,
                out var candidate,
                out var memberError))
        {
            return Failure<ReadPropertyValueResponseDto>(memberError!.Code, memberError.Message ?? "The member reference is unavailable.");
        }

        if (candidate!.Property is null)
        {
            return Failure<ReadPropertyValueResponseDto>(
                OperationErrorCode.InvalidMemberReference,
                "The requested member is not a property.");
        }

        var invocation = await scheduler
            .InvokeAsync(
                acquisition.Target,
                () => candidate.Property.GetValue(acquisition.Target, index: null),
                cancellationToken)
            .ConfigureAwait(false);
        if (invocation.OperationError is not null)
        {
            return ClrInspectionResult<ReadPropertyValueResponseDto>.Failure(invocation.OperationError);
        }

        if (invocation.Exception is not null)
        {
            var targetException = CreateTargetException(invocation.Exception);
            if (targetException is null)
            {
                return Failure<ReadPropertyValueResponseDto>(
                    OperationErrorCode.RuntimeUnavailable,
                    "The target exception type could not be described.");
            }

            return ClrInspectionResult<ReadPropertyValueResponseDto>.Success(
                new ReadPropertyValueResponseDto(
                    new MemberReadResultDto(
                        request.Member,
                        ClrReadOutcome.TargetFailed,
                        targetException: targetException)));
        }

        var conversion = new ClrValueConverter(_session).Convert(invocation.Value);
        return ClrInspectionResult<ReadPropertyValueResponseDto>.Success(
            new ReadPropertyValueResponseDto(CreateReadResult(request.Member, conversion)));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _memberRegistry.Clear();
            CryptographicOperations.ZeroMemory(_continuationKey);
        }
    }

    private bool TryBuildCandidates(
        object target,
        string? boundaryId,
        ClrMemberKindFilter filter,
        out CandidateSet? set,
        out OperationErrorDto? error)
    {
        set = null;
        error = null;
        if (boundaryId is null)
        {
            error = new OperationErrorDto(
                OperationErrorCode.InvalidMemberReference,
                "The target handle has no runtime boundary.");
            return false;
        }

        if (!_session.TryGetOrCreateTypeIdentity(target.GetType(), out var typeIdentity)
            || typeIdentity is null)
        {
            error = new OperationErrorDto(
                OperationErrorCode.RuntimeUnavailable,
                "The target type could not be described.");
            return false;
        }

        var candidates = new List<MemberCandidate>();
        var hierarchyPosition = 0;
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            if (filter is ClrMemberKindFilter.All or ClrMemberKindFilter.Fields)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (!TryCreateCandidate(field, boundaryId, hierarchyPosition, out var candidate))
                    {
                        continue;
                    }

                    candidates.Add(candidate!);
                }
            }

            if (filter is ClrMemberKindFilter.All or ClrMemberKindFilter.Properties)
            {
                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    var getter = property.GetGetMethod(nonPublic: false);
                    if (getter is null || property.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }

                    if (!TryCreateCandidate(property, getter, boundaryId, hierarchyPosition, out var candidate))
                    {
                        continue;
                    }

                    candidates.Add(candidate!);
                }
            }

            hierarchyPosition++;
        }

        if (candidates.Count > ClrInspectionLimits.MaxMembersPerType)
        {
            error = new OperationErrorDto(
                OperationErrorCode.RegistryQuotaExceeded,
                "The target type exceeded the maximum member count.");
            return false;
        }

        candidates.Sort(MemberCandidateComparer.Instance);
        var fingerprint = ComputeFingerprint(candidates.Select(candidate => candidate.Key.CanonicalText()));
        set = new CandidateSet(typeIdentity, candidates, fingerprint);
        return true;
    }

    private bool TryCreateCandidate(
        FieldInfo field,
        string boundaryId,
        int hierarchyPosition,
        out MemberCandidate? candidate)
    {
        candidate = null;
        if (!_session.TryGetOrCreateTypeIdentity(field.DeclaringType!, out var declaringIdentity)
            || declaringIdentity is null
            || !_session.TryGetOrCreateTypeIdentity(field.FieldType, out var valueIdentity)
            || valueIdentity is null)
        {
            return false;
        }

        var key = new MemberStructuralKey(
            ClrMemberKind.Field,
            boundaryId,
            declaringIdentity.TypeId,
            field.Name,
            valueIdentity.TypeId,
            Array.Empty<string>(),
            field.Name,
            field.IsStatic,
            TryGetModuleVersionId(field.Module),
            TryGetMetadataToken(field));
        candidate = new MemberCandidate(
            key,
            field.Name,
            new TypeRefDto(valueIdentity.TypeId, valueIdentity.BoundaryId),
            $"{field.FieldType.FullName ?? field.FieldType.Name} {field.Name}",
            hierarchyPosition,
            field,
            null);
        return true;
    }

    private bool TryCreateCandidate(
        PropertyInfo property,
        MethodInfo getter,
        string boundaryId,
        int hierarchyPosition,
        out MemberCandidate? candidate)
    {
        candidate = null;
        if (!_session.TryGetOrCreateTypeIdentity(property.DeclaringType!, out var declaringIdentity)
            || declaringIdentity is null
            || !_session.TryGetOrCreateTypeIdentity(property.PropertyType, out var valueIdentity)
            || valueIdentity is null)
        {
            return false;
        }

        var indexTypes = property.GetIndexParameters()
            .Select(parameter => _session.TryGetOrCreateTypeIdentity(parameter.ParameterType, out var identity)
                ? identity?.TypeId
                : null)
            .Where(static value => value is not null)
            .Select(static value => value!)
            .ToArray();
        if (indexTypes.Length != property.GetIndexParameters().Length)
        {
            return false;
        }

        var key = new MemberStructuralKey(
            ClrMemberKind.Property,
            boundaryId,
            declaringIdentity.TypeId,
            property.Name,
            valueIdentity.TypeId,
            indexTypes,
            getter.Name,
            getter.IsStatic,
            TryGetModuleVersionId(getter.Module),
            TryGetMetadataToken(getter));
        candidate = new MemberCandidate(
            key,
            property.Name,
            new TypeRefDto(valueIdentity.TypeId, valueIdentity.BoundaryId),
            $"{property.PropertyType.FullName ?? property.PropertyType.Name} {property.Name}",
            hierarchyPosition,
            null,
            property);
        return true;
    }

    private bool TryResolveMember(
        object target,
        string? boundaryId,
        MemberRefDto member,
        out MemberCandidate? candidate,
        out OperationErrorDto? error)
    {
        candidate = null;
        error = null;
        if (!string.Equals(member.SessionId, _session.SessionId, StringComparison.Ordinal)
            || boundaryId is null
            || !string.Equals(member.BoundaryId, boundaryId, StringComparison.Ordinal)
            || !_memberRegistry.TryGet(member.MemberId, out var record)
            || record is null
            || !string.Equals(record.Key.DeclaringTypeId, member.DeclaringTypeId, StringComparison.Ordinal))
        {
            error = new OperationErrorDto(
                OperationErrorCode.InvalidMemberReference,
                "The member reference is not valid for this Agent session and target.");
            return false;
        }

        if (!TryBuildCandidates(target, boundaryId, ClrMemberKindFilter.All, out var set, out error))
        {
            return false;
        }

        candidate = set!.Candidates.FirstOrDefault(item => item.Key.Equals(record.Key));
        if (candidate is null)
        {
            error = new OperationErrorDto(
                OperationErrorCode.MemberUnavailable,
                "The member is no longer available on the target type.");
            return false;
        }

        return true;
    }

    private TargetExceptionDto? CreateTargetException(Exception exception)
    {
        var wasReflectionWrapper = exception is TargetInvocationException;
        var reportedException = wasReflectionWrapper && exception.InnerException is not null
            ? exception.InnerException
            : exception;
        if (!_session.TryGetOrCreateTypeIdentity(reportedException.GetType(), out var identity)
            || identity is null)
        {
            return null;
        }

        return new TargetExceptionDto(identity, wasReflectionWrapper);
    }

    private static MemberReadResultDto CreateReadResult(
        MemberRefDto member,
        ClrValueConversionResult conversion)
    {
        return new MemberReadResultDto(
            member,
            conversion.Outcome,
            conversion.Value,
            conversion.ErrorCode);
    }

    private string CreateContinuation(
        string boundaryId,
        string typeId,
        ClrMemberKindFilter filter,
        string fingerprint,
        int nextPosition)
    {
        var payload = string.Join(
            "|",
            ContinuationVersion,
            _session.SessionId,
            boundaryId,
            typeId,
            filter,
            fingerprint,
            nextPosition.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var bytes = Encoding.UTF8.GetBytes(payload);
        var signature = HMACSHA256.HashData(_continuationKey, bytes);
        return Base64Url(bytes) + "." + Base64Url(signature);
    }

    private bool TryReadContinuation(
        string token,
        string boundaryId,
        string typeId,
        ClrMemberKindFilter filter,
        string fingerprint,
        out int nextPosition)
    {
        nextPosition = 0;
        if (Encoding.UTF8.GetByteCount(token) > ClrInspectionLimits.MaxContinuationTokenBytes)
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 2
            || !TryFromBase64Url(parts[0], out var payload)
            || !TryFromBase64Url(parts[1], out var signature))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_continuationKey, payload);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            return false;
        }

        var fields = Encoding.UTF8.GetString(payload).Split('|');
        if (fields.Length != 7
            || !string.Equals(fields[0], ContinuationVersion, StringComparison.Ordinal)
            || !string.Equals(fields[1], _session.SessionId, StringComparison.Ordinal)
            || !string.Equals(fields[2], boundaryId, StringComparison.Ordinal)
            || !string.Equals(fields[3], typeId, StringComparison.Ordinal)
            || !string.Equals(fields[4], filter.ToString(), StringComparison.Ordinal)
            || !string.Equals(fields[5], fingerprint, StringComparison.Ordinal)
            || !int.TryParse(
                fields[6],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out nextPosition))
        {
            nextPosition = 0;
            return false;
        }

        return true;
    }

    private static string ComputeFingerprint(IEnumerable<string> keys)
    {
        var text = string.Join("\n", keys);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryFromBase64Url(string value, out byte[] bytes)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += new string('=', (4 - normalized.Length % 4) % 4);
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static int? TryGetMetadataToken(MemberInfo member)
    {
        try
        {
            return member.MetadataToken;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? TryGetModuleVersionId(Module module)
    {
        try
        {
            return module.ModuleVersionId.ToString("D");
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsTargetFailure(Exception exception)
    {
        return exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException
            and not ThreadAbortException;
    }

    private static ClrInspectionResult<T> Failure<T>(OperationErrorCode code, string message)
        where T : class
    {
        return ClrInspectionResult<T>.Failure(new OperationErrorDto(code, message));
    }

    private sealed class CandidateSet
    {
        public CandidateSet(
            TypeIdentityDto typeIdentity,
            IReadOnlyList<MemberCandidate> candidates,
            string fingerprint)
        {
            TypeIdentity = typeIdentity;
            Candidates = candidates;
            Fingerprint = fingerprint;
        }

        public TypeIdentityDto TypeIdentity { get; }

        public IReadOnlyList<MemberCandidate> Candidates { get; }

        public string Fingerprint { get; }
    }

    private sealed class MemberCandidate
    {
        public MemberCandidate(
            MemberStructuralKey key,
            string name,
            TypeRefDto valueType,
            string displaySignature,
            int hierarchyPosition,
            FieldInfo? field,
            PropertyInfo? property)
        {
            Key = key;
            Name = name;
            ValueType = valueType;
            DisplaySignature = displaySignature;
            HierarchyPosition = hierarchyPosition;
            Field = field;
            Property = property;
        }

        public MemberStructuralKey Key { get; }

        public string Name { get; }

        public TypeRefDto ValueType { get; }

        public string DisplaySignature { get; }

        public int HierarchyPosition { get; }

        public FieldInfo? Field { get; }

        public PropertyInfo? Property { get; }
    }

    private sealed class MemberCandidateComparer : IComparer<MemberCandidate>
    {
        public static MemberCandidateComparer Instance { get; } = new();

        public int Compare(MemberCandidate? x, MemberCandidate? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var result = x.HierarchyPosition.CompareTo(y.HierarchyPosition);
            if (result != 0)
            {
                return result;
            }

            result = x.Key.Kind.CompareTo(y.Key.Kind);
            if (result != 0)
            {
                return result;
            }

            result = StringComparer.Ordinal.Compare(x.Name, y.Name);
            if (result != 0)
            {
                return result;
            }

            return StringComparer.Ordinal.Compare(x.Key.CanonicalText(), y.Key.CanonicalText());
        }
    }
}

internal static class ManagedObjectAcquisitionResultExtensions
{
    public static ManagedObjectAcquisition? Require(this ManagedObjectAcquisitionResult result)
    {
        return result.IsSuccess
            ? result.Acquisition
            : null;
    }
}
