namespace NativeSpy.Protocol.Common;

public sealed class HwndInfoDto
{
    public HwndInfoDto(ulong hwnd, string hwndGeneration)
    {
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), hwnd, "An observed HWND must be positive.");
        }

        Hwnd = hwnd;
        HwndGeneration = ContractValidation.RequiredIdentifier(hwndGeneration, nameof(hwndGeneration));
    }

    public ulong Hwnd { get; }

    public string HwndGeneration { get; }
}
