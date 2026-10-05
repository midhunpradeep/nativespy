namespace NativeSpy.Protocol.Json;

internal static class ProtocolJsonCollection
{
    public static IEnumerable<TOutput> MapRequiredElements<TWire, TOutput>(
        IEnumerable<TWire?>? values,
        Func<TWire, TOutput> converter,
        string description)
        where TWire : class
    {
        if (values is null)
        {
            throw new ProtocolJsonException($"The {description} collection is missing.");
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ProtocolJsonException($"The {description} contains a null element.");
            }

            yield return converter(value);
        }
    }
}
