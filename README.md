# Inceptus Document Engine

A reusable BPMN modeler with immutable native documents and a standalone process presentation export. This README describes the coordinated **0.1.4 development line** targeting **net10.0**. Development preparation does not mean the packages have been published to a public feed.

[Product page](https://inceptus.online/bpmn/) · [System/application](https://bpmn.inceptus.online)

[Public release source](https://github.com/INCEPTUS-LABS/DocumentEngine) · [Building and releasing](https://github.com/INCEPTUS-LABS/DocumentEngine/blob/main/docs/releasing.md)

## Choose an integration level

| Package | Intended entry point |
| --- | --- |
| `Inceptus.DocumentEngine.Bpmn.Blazor` | Ordinary BPMN modeler integration through the component and public facade below. |
| `Inceptus.DocumentEngine.Contracts` | Advanced, notation-neutral immutable models and extension contracts; the snapshot graph is also part of ordinary facade input/output. |
| `Inceptus.DocumentEngine.Runtime` | Advanced headless document, command, processing and History infrastructure. |
| `Inceptus.DocumentEngine.Bpmn` | Advanced BPMN semantics, commands, validation and plugin contributions. |
| `Inceptus.DocumentEngine.Organizational` | Advanced Organizational profile composition, independent of the browser renderer. |
| `Inceptus.DocumentEngine.Canvas2D` | Advanced browser/session integration, generic Canvas2D rendering and standalone Publish construction. |

Use the six packages as an **aligned version family**. The top-level modeler package brings the lower packages transitively; ordinary hosts need only that explicit Inceptus reference. Independent mixed family versions are unsupported and unverified. Canvas2D requires its matching Runtime version because it consumes Runtime's internal History contract. Other dependency ranges retain their existing minimum-version policy. A direct dependency override can still produce an incompatible graph and a NuGet warning; the constraint is not a runtime compatibility guarantee.

## Embed the modeler

The validated hosting model is a **standalone Blazor WebAssembly application** on .NET 10. Configure a feed containing the reviewed candidate before adding the package:

```xml
<PackageReference Include="Inceptus.DocumentEngine.Bpmn.Blazor" Version="0.1.4" />
```

Register the modeler in the application's `Program.cs`. The extension namespace is `Microsoft.Extensions.DependencyInjection`:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddInceptusBpmnModeler();
```

Render the public component in a container with usable dimensions:

```razor
@using Inceptus.DocumentEngine.Bpmn.Blazor
@using Inceptus.DocumentEngine.Bpmn.Blazor.Components
@using Inceptus.DocumentEngine.Contracts.Documents

<div style="height: 75vh; min-height: 24rem;">
    <InceptusBpmnModeler @ref="_modeler"
                        InitialDocument="InitialSnapshot"
                        Ready="OnReady"
                        DocumentChanged="OnDocumentChanged"
                        OperationFailed="OnOperationFailed"
                        aria-label="Process modeler" />
</div>
<p>@_lastSnapshot?.DocumentId @_operationStatus</p>

@code {
    [Parameter] public DocumentSnapshot? InitialSnapshot { get; set; }
    private InceptusBpmnModeler? _modeler;
    private DocumentSnapshot? _lastSnapshot;
    private string? _operationStatus;

    private void OnReady(BpmnModelerReadyEventArgs args)
        => _lastSnapshot = args.Snapshot;

    private void OnDocumentChanged(BpmnModelerDocumentChangedEventArgs args)
        => _lastSnapshot = args.Snapshot;

    private void OnOperationFailed(BpmnModelerOperationFailedEventArgs args)
        => _operationStatus = $"{args.Operation}: {args.Status}";
}
```

Keep the host's generated scoped-CSS bundle linked in `wwwroot/index.html`, using the **application's assembly name**:

```html
<link href="YourApplication.styles.css" rel="stylesheet" />
```

Razor static-web-asset integration supplies the package's JavaScript, scoped CSS and font. Do not copy modeler assets or reference engine source projects in a package consumer. Keep ordinary Blazor framework bootstrapping and the host's application base configuration. Root and non-root deployment use the same package assets; there are no modeler-specific asset URL options.

`Class`, `Style` and `AdditionalAttributes` (`IReadOnlyDictionary<string, object>?`, captured unmatched attributes) apply to the component wrapper. Ensure ancestor layout allows it to receive width and height.

## UI localization

The modeler UI supports English (`en` / `en-GB`), Polish (`pl` / `pl-PL`), French (`fr` / `fr-FR`), German (`de` / `de-DE`) and Spanish (`es` / `es-ES`). English is the neutral resource and final fallback. Standard .NET resource resolution also supports parent fallback: `fr-CA`, `de-AT` and `es-MX` use French, German and Spanish; `en-US` and unsupported cultures such as `it-IT` use English.

`CultureInfo.CurrentUICulture` is the sole culture authority. `AddInceptusBpmnModeler()` includes standard localization registration; consumers do not need internal resource types. The RCL carries its resources and satellite assemblies. There is no public `Language` or `Culture` parameter and no built-in language selector.

For runtime switching, change the host UI culture in its normal Blazor rendering context and re-render. A host can cascade that culture to ensure even a parameterless modeler receives the render notification:

```razor
@using System.Globalization

<CascadingValue Value="CultureInfo.CurrentUICulture">
    <InceptusBpmnModeler />
</CascadingValue>
```

The cascade is a render notification, not an override: set the ambient `CurrentUICulture` as well as updating the host render. The host remains responsible for its normal culture lifetime and WebAssembly globalization/ICU configuration. Changing culture does not replace the Document or editing session, write History, reset selection/viewport, or invoke `DocumentChanged`. Open dialogs and feedback resolve presentation labels again on rendering.

Document names, property values, diagnostic identities and canonical diagnostic messages remain unchanged. Native import/export and PublishedProcess use their existing English-based, culture-invariant schemas; there is no public data-format change. Only UI wrappers around canonical diagnostics are localized. The standalone exported PublishedProcess viewer remains outside modeler localization scope. Host-owned labels and documentation remain the host's responsibility.

## Document ownership and notifications

The component owns its live Document, editing session, History and browser resources. Hosts exchange immutable `DocumentSnapshot` graphs, immutable diagnostic/result objects and copied file bytes. Do not retain a mutable engine runtime through ordinary integration.

`InitialDocument` is consumed **once**, at initialization. A supplied snapshot takes precedence over the advanced startup provider. Null uses the provider if one is registered, otherwise canonical empty startup. Later parameter changes do not replace the document: call `LoadDocumentAsync` explicitly. Two components reconstruct independent live state even from the same initial snapshot.

- `Ready`: `EventCallback<BpmnModelerReadyEventArgs>`, once after successful initial attachment; `Snapshot` is the initial persistent state. Attachment alone does not emit `DocumentChanged`.
- `DocumentChanged`: `EventCallback<BpmnModelerDocumentChangedEventArgs>`; exposes `Snapshot` and `Kind` (`PersistentMutation` or `DocumentReplacement`). Accepted persistent edits, Undo/Redo and replacements notify; selection, pan/zoom, scope navigation, validation alone and transient visibility do not.
- `OperationFailed`: `EventCallback<BpmnModelerOperationFailedEventArgs>`; exposes `Operation`, `Status` and `ImmutableArray<Diagnostic> Diagnostics`. These are bounded operation diagnostics, separate from model-validation Issues. Expected cancellation does not emit a failure callback. Context-edit rejections use the editor's interaction diagnostics, not an additional facade callback kind.

Callback **invocation** is ordered. Arbitrary asynchronous consumer callbacks may complete in a different order and may await subsequent modeler operations. Callback exceptions do not roll back committed changes. Track DocumentId together with revision: replacing a document can legitimately lower the revision number. Disposal and retired sessions suppress stale notifications.

## Public lifecycle and file operations

The component namespace is `Inceptus.DocumentEngine.Bpmn.Blazor.Components`. Result/event types are in `Inceptus.DocumentEngine.Bpmn.Blazor`; `DocumentSnapshot` is in `Inceptus.DocumentEngine.Contracts.Documents`.

```csharp
BpmnModelerDocumentResult CaptureDocumentSnapshot();
ValueTask<BpmnModelerDocumentResult> NewDocumentAsync(
    CancellationToken cancellationToken = default);
ValueTask<BpmnModelerDocumentResult> LoadDocumentAsync(
    DocumentSnapshot? document, CancellationToken cancellationToken = default);
ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync(
    ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken = default);
ValueTask<BpmnModelerFileResult> ExportNativeDocumentAsync(
    CancellationToken cancellationToken = default);
ValueTask<BpmnModelerFileResult> PublishAsync(
    CancellationToken cancellationToken = default);
```

Invoke operations after Ready and inspect `Succeeded`, `Status` and `Diagnostics`. Status is one of `Succeeded`, `Rejected`, `Unavailable`, `Failed`, `Cancelled`. Successful document results have a `Snapshot`; successful file results have an `Artifact`. On other outcomes these payloads are null. `BpmnModelerFileArtifact` exposes `FileName`, `ContentType` and immutable byte `Content`.

Capture and Export do not change persistent state, History, selection or viewport. Programmatic New creates a fresh empty identity without displaying the toolbar confirmation dialog. Load and Import reconstruct the supplied persistent identity, revision and content into a fresh session with fresh History, without mutating caller input. Replacement is not an undoable document-edit command. Failed or cancelled candidate replacement leaves the previous document authoritative and usable.

Native import/export uses **Inceptus.Document JSON, formatVersion 1**. Public byte APIs do not open a file picker or initiate a download; the consumer chooses storage/delivery. The toolbar retains its own file and confirmation UI. Export, Import and Load are not persistence or autosave services.

## Publish a standalone presentation

`PublishAsync` captures the current active Process scope into a self-contained ZIP. It does not change Publication metadata, DocumentId, revision, persistent content or History, and does not initiate a download. Inspect the result before delivering its bytes. The toolbar's separate Publication dialog can save changed valid metadata through one persistent command **before** generating the artifact; a later Publish rejection does not undo that save.

The artifact remains **Inceptus.PublishedProcess, formatVersion 1**, with these entries:

```text
index.html
process.json
process.data.js
inceptus.publish.js
styles.css
```

`process.data.js` contains the same PublishedProcess object as `process.json`. Classic scripts and static bootstrap data allow static HTTP hosting and direct-file startup without fetching JSON at runtime. Keep the generated files together with their matching viewer. The standalone output needs no authoring host, source checkout, Blazor, .NET or WASM runtime. Browser/security policy may restrict local `file://` access.

Publication metadata and element descriptions are captured as data. Descriptions are not displayed by the viewer. Node geometry and connector routes are frozen. Pools are treated as expanded for publication, and Pool/Unassigned graphics are omitted. Diagram editing is unavailable; pointer drag and wheel/touchpad pan, and +/- controls zoom the model.

Publication supports **one or more Start-role nodes** in the active scope. Reachable paths from every Start are included without cloning shared identities. Each clicked Start activates exactly one token at that node; repeated clicks create independent activations. With multiple Starts the global convenience button is disabled and guides the user to click a Start in the diagram. Zero Starts or an invalid supported path produces blocking diagnostics. Supporting several Starts does not relax individual node/connector validation or introduce process-instance correlation.

The marketing/demo viewer retains lightweight token behavior: Activity waits 2000 ms per token; SplitInvariant chooses one outgoing branch; MergeInvariant consumes tokens from two distinct inputs and emits one; ParallelSynchronize waits for every input, consumes one from each and emits on every output; End consumes tokens. This is not a production process execution or Simulation Engine.

Editor Publish failures remain visible outside the closed Publication dialog, with returned reasons and affected identities when available. Successful retry or session replacement clears obsolete feedback. Feedback is transient and does not enter Document History or persistent Issues.

## Advanced and implementation-facing surfaces

Lower-layer APIs support deliberate headless or browser/session composition, with their own ownership and extension contracts. The modeler's RCL remains the ordinary composition authority. Historical `BpmnPluginRegistration` milestone selectors are advanced historical compositions, not interchangeable support levels; the ordinary RCL currently uses `N100` and adds Organizational independently.

`IBpmnModelerStartupDocumentProvider.GetInitialDocumentAsync(CancellationToken cancellationToken = default)` returns `ValueTask<Inceptus.DocumentEngine.Runtime.Documents.Document>`. This advanced startup-only seam transfers ownership of a **fresh Document per component**; it is not a live model handle, replacement callback or persistence service. Prefer `InitialDocument` for ordinary startup.

The publicly generated `DocumentCanvas` and `ToolboxPanel` components are implementation-facing. Their visibility does not make them supported alternatives to `InceptusBpmnModeler`. The standalone `PublishedTokenRuntime.activateStart(startId, atMs)` method and frozen `startNodeIds` observation are advanced runtime integration surfaces; use the artifact's matching runtime. Viewer gesture helpers, `startAt` and control wiring are implementation-facing, not ordinary modeler APIs or a separate stable JavaScript SDK.

## Support limits and release identity

Acceptance covers the tested standalone Blazor WebAssembly host and recorded browser conditions. It does not establish Interactive Server support, every browser/device, BPMN XML interoperability, arbitrary cross-version API compatibility or production workflow execution. Native Inceptus.Document and standalone PublishedProcess are distinct formats.

An ID/version identifies immutable distributed package bytes. Recover an existing release only from retained hash-matching artifacts. Rebuilding the same source version does not recreate release identity; unrecoverable bytes require a deliberate new candidate/version. Candidate promotion and external publication are separate decisions.

The public source repository contains deliberate release snapshots. Public packages must be built from the corresponding public commit, which supplies repository commit metadata and SDK-native Source Link mappings. Version 0.1.3 prepares portable `.snupkg` symbols alongside each package. Source stepping requires the matching published symbols and publicly retrievable source; preparation alone does not establish nuget.org availability. Local application breakpoints remain distinct from stepping into packaged implementation.

First-party code is licensed under [MIT](https://github.com/INCEPTUS-LABS/DocumentEngine/blob/main/LICENSE), Copyright (c) 2026 Inceptus Robert Prokopczuk. Redistributed third-party material retains its own terms; see [third-party notices](https://github.com/INCEPTUS-LABS/DocumentEngine/blob/main/THIRD-PARTY-NOTICES.md), including the separately bundled DejaVu font license. See [release notes](https://github.com/INCEPTUS-LABS/DocumentEngine/blob/main/RELEASE-NOTES.md). These documents are also included in each primary package.
