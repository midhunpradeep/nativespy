using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CallbackDetailDto
{
    public CallbackDetailDto(string name, int? count = null, bool countKnown = false)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Callback count cannot be negative.");
        }

        if (countKnown && count is null)
        {
            throw new ArgumentException("A known callback count must be supplied.", nameof(count));
        }

        if (!countKnown && count is not null)
        {
            throw new ArgumentException("An unknown callback count must be omitted.", nameof(count));
        }

        Count = count;
        CountKnown = countKnown;
    }

    public string Name { get; }

    public int? Count { get; }

    public bool CountKnown { get; }
}
