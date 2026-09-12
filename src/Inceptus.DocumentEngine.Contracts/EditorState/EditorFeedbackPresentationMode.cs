namespace Inceptus.DocumentEngine.Contracts.EditorState;

/// <summary>
/// Controls whether temporary editor feedback receives the framework-owned generic presentation.
/// Registered scene contributors always receive the feedback snapshot.
/// </summary>
public enum EditorFeedbackPresentationMode
{
    Default = 0,
    ContributorOnly = 1,
}
