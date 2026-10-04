namespace NativeSpy.FlaUI;

public static class FlaUiQuarantineBudget
{
    private const int MaximumQuarantinedExecutors = 2;
    private static int _quarantinedExecutors;

    internal static bool TryReserve()
    {
        while (true)
        {
            var current = Volatile.Read(ref _quarantinedExecutors);
            if (current >= MaximumQuarantinedExecutors)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _quarantinedExecutors, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    internal static void Release()
    {
        Interlocked.Decrement(ref _quarantinedExecutors);
    }
}
