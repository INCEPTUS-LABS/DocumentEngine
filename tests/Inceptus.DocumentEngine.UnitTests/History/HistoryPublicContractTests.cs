using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class HistoryPublicContractTests
{
    private static readonly DocumentId DocumentId = new("test:history-public");
    private static readonly CommandTypeId CommandTypeId = new("test:history-command");

    [Fact]
    public void StatusAndOperationResultsAreImmutableSnapshotFreeValues()
    {
        var status = new HistoryStatus(2, canUndo: true, canRedo: false);
        var diagnostics = new List<Diagnostic>
        {
            new("test:history-info", DiagnosticSeverity.Information, "History information."),
        };
        var result = HistoryOperationResult.CreateCommitted(
            DocumentId,
            CommandTypeId,
            new DocumentRevision(4),
            new DocumentRevision(5),
            status,
            diagnostics);
        diagnostics.Clear();

        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(5), result.CommittedRevision);
        Assert.Single(result.Diagnostics);
        Assert.Equal(new HistoryStatus(2, true, false), result.HistoryStatus);
        Assert.DoesNotContain(
            typeof(HistoryOperationResult).GetProperties(),
            property => property.PropertyType == typeof(DocumentSnapshot) ||
                typeof(ICommand).IsAssignableFrom(property.PropertyType));
        Assert.All(
            typeof(HistoryOperationResult).GetProperties(),
            property => Assert.False(property.CanWrite));
        Assert.All(
            typeof(HistoryStatus).GetProperties(),
            property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void NonCommittedResultCarriesNoCommittedRevision()
    {
        var result = HistoryOperationResult.CreateNotCommitted(
            DocumentId,
            commandTypeId: null,
            HistoryOperationStatus.NothingToUndo,
            new DocumentRevision(7),
            new HistoryStatus(0, false, false));

        Assert.False(result.IsCommitted);
        Assert.Null(result.CommandTypeId);
        Assert.Null(result.CommittedRevision);
        Assert.Equal(new DocumentRevision(7), result.PreviousRevision);
    }

    [Fact]
    public void RuntimeAppliedResultIsSuccessfulWithoutCommandOrRevisionCommit()
    {
        var status = new HistoryStatus(1, canUndo: false, canRedo: true);
        var result = HistoryOperationResult.CreateApplied(
            DocumentId,
            new DocumentRevision(7),
            status);

        Assert.True(result.Succeeded);
        Assert.True(result.IsApplied);
        Assert.False(result.IsCommitted);
        Assert.Equal(HistoryOperationStatus.Applied, result.Status);
        Assert.Null(result.CommandTypeId);
        Assert.Null(result.CommittedRevision);
        Assert.Equal(new DocumentRevision(7), result.PreviousRevision);
        Assert.Equal(status, result.HistoryStatus);
        Assert.Throws<ArgumentException>(() => HistoryOperationResult.CreateNotCommitted(
            DocumentId,
            commandTypeId: null,
            HistoryOperationStatus.Applied,
            new DocumentRevision(7),
            status));
    }

    [Fact]
    public void HistoryStatusRejectsImpossibleAvailability()
    {
        Assert.Throws<ArgumentException>(() => new HistoryStatus(0, true, false));
        Assert.Throws<ArgumentException>(() => new HistoryStatus(1, false, false));
    }

    [Fact]
    public void HistoryManagerIsBoundToOneDocumentAndExposesOnlyNarrowOperations()
    {
        var document = Assert.IsType<Document>(
            DocumentFactory.CreateEmpty(DocumentId).Document);
        var manager = new HistoryManager(document);
        var methods = typeof(HistoryManager).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(new HistoryStatus(0, false, false), manager.CaptureStatus());
        Assert.Equal(
            ["CaptureStatus", "ExecuteAsync", "RedoAsync", "UndoAsync"],
            methods.Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.Empty(typeof(HistoryManager).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(HistoryManager).GetEvents(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.DoesNotContain(methods.SelectMany(method => method.GetParameters()), parameter =>
            parameter.ParameterType == typeof(DocumentSnapshot) ||
            parameter.ParameterType == typeof(HistoryStore));
        Assert.Contains(methods, method =>
            method.Name == nameof(HistoryManager.ExecuteAsync) &&
            method.GetParameters()[0].ParameterType == typeof(CommandProcessor));
    }
}
