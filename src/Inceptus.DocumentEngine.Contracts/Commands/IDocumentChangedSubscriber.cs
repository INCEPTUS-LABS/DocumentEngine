namespace Inceptus.DocumentEngine.Contracts.Commands;

public interface IDocumentChangedSubscriber
{
    ValueTask OnDocumentChangedAsync(DocumentChangedEvent change);
}
