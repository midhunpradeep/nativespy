namespace NativeSpy.Client.Correlation;

internal static class InternalValidation
{
    public static string RequiredIdentifier(string? value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An identifier must not be empty or whitespace.", parameterName);
        }

        return value;
    }

    public static IReadOnlyList<T> CopyRequired<T>(IEnumerable<T>? values, string parameterName)
    {
        if (values is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("Collections must not contain null elements.", parameterName);
        }

        return copy;
    }

    public static void RequireUniqueIdentifiers(IEnumerable<string> values, string parameterName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw new ArgumentException("Identifier entries must be unique.", parameterName);
            }
        }
    }
}
