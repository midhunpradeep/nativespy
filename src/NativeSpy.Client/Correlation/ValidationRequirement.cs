namespace NativeSpy.Client.Correlation;

internal sealed class ValidationRequirement
{
    public ValidationRequirement(string checkName)
    {
        CheckName = InternalValidation.RequiredIdentifier(checkName, nameof(checkName));
    }

    public string CheckName { get; }
}
