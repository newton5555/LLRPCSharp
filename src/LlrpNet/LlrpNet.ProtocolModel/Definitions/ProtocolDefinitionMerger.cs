namespace LlrpNet.ProtocolModel.Definitions;

/// <summary>
/// Combines a complete protocol baseline with a protocol delta.
/// </summary>
/// <remarks>
/// Additions may not collide with baseline definitions. Replacements are accepted only
/// through the explicit override collection and retain their baseline wire identity.
/// </remarks>
public static class ProtocolDefinitionMerger
{
    /// <summary>
    /// Applies explicit replacements and additions to <paramref name="baseline"/>.
    /// </summary>
    public static ProtocolDefinition Merge(
        ProtocolDefinition baseline,
        ProtocolDefinition additions,
        ProtocolDefinition overrides,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(overrides);

        return new ProtocolDefinition(
            sourceName ?? additions.SourceName,
            additions.XmlNamespace ?? baseline.XmlNamespace,
            Apply(baseline.Messages, additions.Messages, overrides.Messages, static item => item.Name, "message", static (before, after) => before.TypeNumber == after.TypeNumber),
            Apply(baseline.Parameters, additions.Parameters, overrides.Parameters, static item => item.Name, "parameter", static (before, after) => before.TypeNumber == after.TypeNumber),
            Apply(baseline.Enumerations, additions.Enumerations, overrides.Enumerations, static item => item.Name, "enumeration", static (_, _) => true),
            Apply(baseline.Choices, additions.Choices, overrides.Choices, static item => item.Name, "choice", static (_, _) => true),
            Apply(baseline.Vendors, additions.Vendors, overrides.Vendors, static item => item.Name, "vendor", static (before, after) => before.VendorId == after.VendorId),
            Apply(baseline.CustomMessages, additions.CustomMessages, overrides.CustomMessages, static item => item.Name, "custom message", static (before, after) => before.Vendor == after.Vendor && before.Subtype == after.Subtype),
            Apply(baseline.CustomParameters, additions.CustomParameters, overrides.CustomParameters, static item => item.Name, "custom parameter", static (before, after) => before.Vendor == after.Vendor && before.Subtype == after.Subtype),
            Apply(baseline.CustomEnumerations, additions.CustomEnumerations, overrides.CustomEnumerations, static item => item.Name, "custom enumeration", static (before, after) => before.Namespace == after.Namespace));
    }

    private static IReadOnlyList<T> Apply<T>(
        IReadOnlyList<T> baseline,
        IReadOnlyList<T> additions,
        IReadOnlyList<T> overrides,
        Func<T, string> name,
        string kind,
        Func<T, T, bool> preservesIdentity)
        where T : class
    {
        var result = baseline.ToList();
        var indexes = baseline
            .Select((item, index) => new KeyValuePair<string, int>(name(item), index))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);

        foreach (T replacement in overrides)
        {
            string replacementName = name(replacement);
            if (!indexes.TryGetValue(replacementName, out int index))
            {
                throw new InvalidOperationException($"The override does not match a baseline {kind}: '{replacementName}'.");
            }

            if (!preservesIdentity(result[index], replacement))
            {
                throw new InvalidOperationException($"The override changes the wire identity of {kind} '{replacementName}'.");
            }

            result[index] = replacement;
        }

        var names = new HashSet<string>(indexes.Keys, StringComparer.Ordinal);
        foreach (T addition in additions)
        {
            if (!names.Add(name(addition)))
            {
                throw new InvalidOperationException(
                    $"The additive delta duplicates {kind} '{name(addition)}'. Use an explicit override to replace a baseline definition.");
            }
        }

        result.AddRange(additions);
        return result;
    }
}
