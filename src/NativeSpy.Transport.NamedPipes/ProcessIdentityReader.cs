using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Transport.NamedPipes;

public static class ProcessIdentityReader
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static ProcessIdentityDto ReadCurrent()
    {
        return ReadForProcessId(Environment.ProcessId);
    }

    public static ProcessIdentityDto ReadForProcessId(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "ProcessId must be positive.");
        }

        using var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            false,
            processId);
        if (processHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The target process could not be opened.");
        }

        if (!GetProcessTimes(
                processHandle,
                out var creationTime,
                out _,
                out _,
                out _))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The target process creation time could not be read.");
        }

        var fileTime = ((ulong)creationTime.dwHighDateTime << 32) | creationTime.dwLowDateTime;
        return new ProcessIdentityDto(
            processId,
            fileTime.ToString(CultureInfo.InvariantCulture));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle processHandle,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }
}
