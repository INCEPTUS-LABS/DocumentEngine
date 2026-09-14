using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed record DocumentCanvasScopeNavigationContextAction(
    SemanticElementId OwnerSemanticElementId,
    SemanticTypeId SemanticTypeId,
    VisualStateId VisualStateId,
    DocumentScopeId TargetScopeId,
    string Label);

internal sealed record DocumentCanvasScopeBreadcrumbSegment(
    DocumentScopeId ScopeId,
    string Label,
    bool IsActive,
    string? LabelResourceKey = null);

internal static class DocumentCanvasScopeBreadcrumb
{
    internal const string RootLabel = "Main Process";
    internal const string FallbackNestedLabel = "Scope";

    internal static ImmutableArray<DocumentCanvasScopeBreadcrumbSegment> Empty => [];
}
