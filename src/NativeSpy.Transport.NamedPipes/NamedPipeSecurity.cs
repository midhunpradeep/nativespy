using System.Security.Principal;

namespace NativeSpy.Transport.NamedPipes;

public static class NamedPipeSecurity
{
    public static SecurityIdentifier GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User
            ?? throw new InvalidOperationException("The current Windows identity has no user SID.");
    }
}
