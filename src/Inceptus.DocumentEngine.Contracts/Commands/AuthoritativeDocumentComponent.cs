namespace Inceptus.DocumentEngine.Contracts.Commands;

[Flags]
public enum AuthoritativeDocumentComponent
{
    None = 0,
    SemanticModel = 1,
    VisualModel = 2,
    Metadata = 4,
    Publication = 8,
}
