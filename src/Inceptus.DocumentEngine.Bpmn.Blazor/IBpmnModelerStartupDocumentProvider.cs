using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>
/// Supplies the initial <see cref="Document"/> for a newly created BPMN modeler instance.
/// </summary>
/// <remarks>
/// Each invocation must return a fresh <see cref="Document"/> instance intended for ownership by
/// exactly one modeler instance. The provider does not participate in later document replacement,
/// persistence, modeler composition, or change notification.
/// </remarks>
public interface IBpmnModelerStartupDocumentProvider
{
    /// <summary>
    /// Creates or reconstructs the initial Document for one new modeler instance.
    /// </summary>
    ValueTask<Document> GetInitialDocumentAsync(
        CancellationToken cancellationToken = default);
}
