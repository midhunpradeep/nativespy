using NativeSpy.Protocol.Clr;

namespace NativeSpy.Agent;

public static class ClrInspectionLimits
{
    public const int MaxMembersPerPage = ClrInspectionContractLimits.MaxMembersPerPage;
    public const int MaxFieldsPerBatch = ClrInspectionContractLimits.MaxFieldsPerBatch;
    public const int MaxStringCodeUnits = ClrInspectionContractLimits.MaxStringCodeUnits;
    public const int MaxStructFields = ClrInspectionContractLimits.MaxStructFields;
    public const int MaxStructPayloadBytes = 16 * 1024;
    public const int MaxNewObjectReferencesPerResponse = 64;
    public const int MaxObjectRecordsPerSession = 16384;
    public const int MaxMemberRecordsPerSession = 16384;
    public const int MaxMembersPerType = 4096;
    public const int MaxContinuationTokenBytes = ClrInspectionContractLimits.MaxContinuationTokenBytes;
}
