using System.Collections.ObjectModel;
using LlrpNet.ProtocolModel.Definitions;

namespace LlrpNet.ProtocolModel.Validation;

/// <summary>
/// Supplies already imported protocol definitions whose symbols may be referenced by the definition being validated.
/// </summary>
public sealed class ProtocolDefinitionValidationContext
{
    /// <summary>
    /// Initializes a validation context with dependency definitions in lookup order.
    /// </summary>
    public ProtocolDefinitionValidationContext(IEnumerable<ProtocolDefinition> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ProtocolDefinition[] copy = dependencies.ToArray();
        if (copy.Any(static dependency => dependency is null))
        {
            throw new ArgumentException("A dependency collection cannot contain null entries.", nameof(dependencies));
        }

        Dependencies = new ReadOnlyCollection<ProtocolDefinition>(copy);
    }

    /// <summary>
    /// Gets imported dependency definitions used only for symbol resolution.
    /// </summary>
    public IReadOnlyList<ProtocolDefinition> Dependencies { get; }
}
