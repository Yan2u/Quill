namespace Quill.VariableXaml.Model;

/// <summary>
/// A typed attribute value with a stable 64-bit structural hash. The reconciler skips a subtree whose
/// hash is unchanged, so object values (such as the inline content of a text block) must hash their
/// whole structure: a 32-bit <see cref="object.GetHashCode"/> collision would silently skip an update.
/// </summary>
/// <remarks>
/// Implementations must also override <see cref="object.Equals(object?)"/> consistently: attribute
/// patching compares values with it.
/// </remarks>
public interface IStructuralHash
{
    ulong StructuralHash { get; }
}
