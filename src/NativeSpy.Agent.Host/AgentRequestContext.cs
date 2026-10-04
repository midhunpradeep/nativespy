using System.Diagnostics;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Host;

public sealed class AgentRequestContext
{
    private readonly long _deadlineTimestamp;

    internal AgentRequestContext(
        ulong requestNumber,
        string requestId,
        string sessionId,
        int protocolVersion,
        ulong budgetMs)
    {
        RequestNumber = requestNumber;
        RequestId = requestId;
        SessionId = sessionId;
        ProtocolVersion = protocolVersion;
        BudgetMs = budgetMs;
        _deadlineTimestamp = AddMilliseconds(Stopwatch.GetTimestamp(), budgetMs);
    }

    public ulong RequestNumber { get; }

    public string RequestId { get; }

    public string SessionId { get; }

    public int ProtocolVersion { get; }

    public ulong BudgetMs { get; }

    public bool IsExpired => Stopwatch.GetTimestamp() >= _deadlineTimestamp;

    public TimeSpan Remaining
    {
        get
        {
            var remainingTicks = _deadlineTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
        }
    }

    private static long AddMilliseconds(long start, ulong milliseconds)
    {
        var seconds = milliseconds / 1000UL;
        var remainder = milliseconds % 1000UL;
        var secondsTicks = seconds > (ulong)(long.MaxValue / Stopwatch.Frequency)
            ? long.MaxValue
            : checked((long)seconds * Stopwatch.Frequency);
        var remainderTicks = checked((long)(remainder * (ulong)Stopwatch.Frequency / 1000UL));
        if (secondsTicks >= long.MaxValue - remainderTicks || start >= long.MaxValue - secondsTicks - remainderTicks)
        {
            return long.MaxValue;
        }

        return start + secondsTicks + remainderTicks;
    }
}

public sealed class AgentOperationDispatchException : Exception
{
    public AgentOperationDispatchException(OperationErrorDto error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public OperationErrorDto Error { get; }
}
