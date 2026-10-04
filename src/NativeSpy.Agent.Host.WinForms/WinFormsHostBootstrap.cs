using System.Security.Principal;
using System.Windows.Forms;
using NativeSpy.Agent.Host;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Host.WinForms;

public static class WinFormsHostBootstrap
{
    public static AgentHost Create(
        Control dispatchAnchor,
        ProcessIdentityDto targetProcessIdentity,
        SecurityIdentifier allowedUserSid)
    {
        if (dispatchAnchor is null)
        {
            throw new ArgumentNullException(nameof(dispatchAnchor));
        }

        var options = AgentHostOptions.CreateDefault(targetProcessIdentity, allowedUserSid);
        return new AgentHost(
            options,
            new WinFormsHostCompositionFactory(dispatchAnchor));
    }
}
