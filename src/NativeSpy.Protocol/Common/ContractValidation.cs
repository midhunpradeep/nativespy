namespace NativeSpy.Protocol.Common;

internal static class ContractValidation
{
    public static TEnum RequireDefinedEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct
    {
        if (!typeof(TEnum).IsEnum || !Enum.IsDefined(typeof(TEnum), value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The enum value is not defined.");
        }

        return value;
    }

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

    public static string RequiredText(string? value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Text must not be empty or whitespace.", parameterName);
        }

        return value;
    }

    public static string? OptionalIdentifier(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return RequiredIdentifier(value, parameterName);
    }

    public static string? OptionalText(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return RequiredText(value, parameterName);
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

    public static IReadOnlyList<T> CopyRequired<T>(IEnumerable<T>? values, string parameterName, IEqualityComparer<T> comparer)
    {
        var copy = CopyRequired(values, parameterName);
        var seen = new HashSet<T>(comparer);
        foreach (var value in copy)
        {
            if (!seen.Add(value))
            {
                throw new ArgumentException("Collection entries must be unique.", parameterName);
            }
        }

        return copy;
    }

    public static IReadOnlyList<TEnum> CopyDefinedEnums<TEnum>(
        IEnumerable<TEnum>? values,
        string parameterName)
        where TEnum : struct
    {
        var copy = CopyRequired(values, parameterName);
        foreach (var value in copy)
        {
            RequireDefinedEnum(value, parameterName);
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
