# Inceptus Document Engine

A reusable BPMN modeler with immutable native documents and a standalone process
presentation export. This README describes the locally prepared **0.1.16** release
candidate targeting **net10.0**; public publication is a separate step.

[Open BPMN Editor](https://bpmn-editor.inceptus.online)

## Choose an integration level

| Package | Intended entry point |
| --- | --- |
| `Inceptus.DocumentEngine.Bpmn.Blazor` | Reusable BPMN editor and immutable document exchange. |
| `Inceptus.Bpmn.Integration` | Native serialization, version compatibility, explicit document validation, BPMN 2.0.2 conversion and publishing package generation. |
| `Inceptus.DocumentEngine.Contracts` | Advanced, notation-neutral immutable models and extension contracts; the snapshot graph is also part of ordinary facade input/output. |
| `Inceptus.DocumentEngine.Runtime` | Advanced headless document, command, processing and History infrastructure. |
| `Inceptus.DocumentEngine.Bpmn` | Advanced BPMN semantics, commands, validation and plugin contributions. |
| `Inceptus.DocumentEngine.Organizational` | Advanced Organizational profile composition, independent of the browser renderer. |
| `Inceptus.DocumentEngine.Canvas2D` | Advanced browser/session integration, generic Canvas2D rendering and standalone Publish construction. |

Use the seven packages as an **aligned version family**. The modeler and Integration are separate entry points: hosts performing document transfer, validation or publishing reference both. Each brings its lower packages transitively. Integration depends on Contracts, Runtime, Bpmn, Organizational and Canvas2D; it does not depend on the editor UI or Demo. Independent mixed family versions are unsupported and unverified. Canvas2D requires its matching Runtime version because it consumes Runtime's internal History contract. Other dependency ranges retain their existing minimum-version policy. A direct dependency override can still produce an incompatible graph and a NuGet warning; the constraint is not a runtime compatibility guarantee.

In 0.1.9, `Inceptus.DocumentEngine.Bpmn` depends on both Contracts and
Organizational. It uses Organizational's Pool membership, Participant assignment
and region geometry for BPMN flow rules and editing. These dependencies resolve
transitively; hosts using only editing still need only the Bpmn.Blazor reference.

## Embed the modeler

The validated hosting model is a **standalone Blazor WebAssembly application** on .NET 10. Configure a feed containing the reviewed candidate before adding the package:

```xml
<PackageReference Include="Inceptus.DocumentEngine.Bpmn.Blazor" Version="0.1.16" />
<PackageReference Include="Inceptus.Bpmn.Integration" Version="0.1.16" />
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

## Source Demo

The canonical source Demo is `src/Inceptus.DocumentEngine.Blazor`. It uses project references to the current engine and starts with its embedded branching order-process sample. Run it with `dotnet run --project src/Inceptus.DocumentEngine.Blazor -c Release`. The sample includes named event-gateway branches, an expandable subprocess and a boundary timer. Its two closely spaced branch labels have explicit placements; Reset label position restores the automatic near-source placement.

The Demo exercises the current activity selection, drag-preview, viewport culling, pan/zoom and renderer-cache paths without opt-in settings. Select activity bodies and captions, move activities, use Undo/Redo, navigate into the subprocess, and pan or zoom the diagram. Selection and viewport changes remain transient; accepted edits use the existing command and History path. The Demo is not packaged; package consumers use the public component described above.

## Editing tools

The main Toolbox provides family-level Events, Activities and Gateways entries,
plus a FLOW group with Sequence Flow and Message Flow. EVENTS, ACTIVITIES,
GATEWAYS and FLOW start collapsed; expand a group to choose a tool. Properties
supports switching compatible concrete Task, intermediate Event, Boundary Event
and Gateway types while preserving identity and compatible state. Invalid
switches are rejected without changing the document.

Properties uses a nine-row Description field with multiline editing and the
existing Apply/Cancel and History behavior. Smart Edit offers **Open SubProcess**
for an eligible selected SubProcess, using the existing scope navigation,
breadcrumb return and navigation History. These controls belong to the packaged
editor; hosts do not need a separate Properties or Smart Edit implementation.

Start and End Events support optional Names in Properties and display nonblank
names as external labels below the event. Newly created Events start unnamed;
clearing a Name removes it. Event Data exposes Name while existing type selectors
and event parameters remain available. Apply and Undo/Redo preserve the exact
previous optional value, identities, attachments and retained geometry. Names
and their resolved text colours are included in graphical exports and published
presentations without changing process execution.

The contextual Properties action now follows the selected element's actual
editable schema, including supported missing values. With Smart Edit enabled,
a completed click on a Sequence Flow or Message Flow opens a compact connector
toolbox containing Properties and Delete near the click. Dragging, reconnecting,
bend editing and modifier selection do not open it. Connector edits and deletion
use the existing Commands and History, including exact Undo/Redo restoration.
Version 0.1.16 corrects Delete for horizontal and vertical connector bounds
without changing node visibility or general rectangle semantics. Message Flow
Undo restores its original scope-owned routing record, including Straight or
Manual routing, path geometry and ordering.

Message Flow connects different effective Participants in the supported Pool
diagram. A standalone Pool supplies its own communication identity; Pools
assigned to the same Participant are presented as adjoining Lanes. Supported
endpoints include eligible Activities, directional Message Events, standalone
Pools and shared Participant labels. Lane-mode Pools are not direct endpoints.
Unassigned elements cannot be Message Flow endpoints.

Auto Arrange reorganizes the active Process scope, including its visible
Pool/Lane diagram, through one undoable edit. It preserves semantic identities,
membership and routing modes; hidden child scopes remain unchanged. Sequence
Flow drives placement. Crossing reduction and connector-point improvement use
bounded heuristics rather than a global optimal-layout guarantee.

Smart Edit starts **ON**. Selecting one eligible flow node shows contextual
Properties / Auto Points / Delete actions and compatible Events / Activities /
Gateways creation choices. Smart Create adds the chosen element and its Sequence
Flow atomically in one History operation, inherits the source's supported
Pool/Lane context, and selects the new element for continued creation. Auto Points
conservatively improves eligible incident connection points. Turning Smart Edit
**OFF** hides both contextual toolboxes while ordinary editing remains available;
turning it ON restores tools eligible for the current selection. Smart Edit and
Toolbox expansion are transient editor UI state, not saved document preferences.

## Document colour schemes

Document Properties offers **White** and **Mixed** colour schemes. White is the
fallback for documents without appearance metadata and retains the established
header presentation. Mixed supplies coordinated colours for Start Events, Tasks,
Gateways, End Events and Pool, Lane and Participant headers. Each scheme retains
its own background, border and text overrides; header colours apply by family,
not to individual Pool/Lane/Participant instances.

**Edit colours** opens the dedicated Colour Scheme editor with seven family
previews, colour pickers, `#RRGGBB` fields and Restore preset. The White Participant
header can retain its default unfilled background. **Use colours** accepts edits
into the parent draft; only Document Properties **Apply** commits them. Cancel
discards the relevant draft, and stale drafts cannot overwrite newer state. The
form follows the Document Properties layout with scrollable content and fixed
actions, and uses the packaged English, Polish, French, German and Spanish UI.

Appearance is stored in the document's existing extension metadata through the
normal validated Command and History path. Undo/Redo restores exact values;
native JSON preserves overrides and unknown metadata. Invalid colour values use
safe presentation fallbacks; unsupported appearance versions are read-only.
Changes recompose the Scene without rearranging nodes or rerouting connectors.
SVG, PNG and PublishedProcess capture resolved paint; execution semantics and
native/published format versions remain unchanged.

These controls and behaviours are supplied by `InceptusBpmnModeler`. Existing
package hosts receive them by updating the aligned package family and retaining
normal Razor static assets and localization setup. No host colour form, connector
toolbox or event-name implementation is required. The internal source Demo's
placement-preview toolbar cache adjustment belongs to its own host orchestration.

## Graphical export and drawing area

The source Demo's Export menu also offers **SVG Image** and **PNG Image**. Both
export the complete current diagram level, including content outside the visible
viewport, independently of zoom and pan. Export does not validate BPMN, modify the
document, enter History or acknowledge native save state.

External hosts capture with `CapturePresentationAsync()` and use the public
`Canvas2DGraphicalExporter` with `GraphicalExportOptions` and their editor's font
and image configuration. `GraphicalExportPreparer` shares preparation across
formats; `SvgGraphicalExporter` also supports managed-only SVG generation. The
host owns menus, downloads and operation feedback.

The editor's internal **Maximize drawing area / Restore drawing area** toggle
uses `BpmnModelerDisplayMode.Normal` / `Maximized`. Bind `DisplayMode` and
`DisplayModeChanged` so the host can hide its own operations toolbar and expand
its container to the available viewport. Keep the same editor mounted. The
packaged component automatically selects Compact toolbox mode on entry and
restores the previous mode on exit, preserving document, selection, zoom, pan
and History.

Two touchscreen contacts pan by their midpoint at fixed zoom; wheel/touchpad
input uses ordered, bounded delivery and compatible-event coalescing. Gesture
takeover safely cancels conflicting editing previews. One-finger touch placement
commits on release, while mouse placement retains its existing behavior. Panning
does not modify the document or History. Hosts receive this through the packaged
CSS/JavaScript and should not add a second gesture handler.

Physical mobile-device, touchscreen and laptop-touchpad acceptance has **not**
been performed. It remains deferred until public package publication and
integration into the standalone InceptusBpmnEditor; browser simulation and
automated coverage do not establish physical-device acceptance.

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

`InitialDocument` is consumed **once**, at initialization. A supplied snapshot takes precedence over the advanced startup provider. Null uses the provider if one is registered, otherwise canonical empty startup. Later parameter changes do not replace the document: call `SetDocumentAsync` explicitly. Two components reconstruct independent live state even from the same initial snapshot.

- `Ready`: `EventCallback<BpmnModelerReadyEventArgs>`, once after successful initial attachment; `Snapshot` is the initial persistent state. Attachment alone does not emit `DocumentChanged`.
- `DocumentChanged`: `EventCallback<BpmnModelerDocumentChangedEventArgs>`; exposes `Snapshot` and `Kind` (`PersistentMutation` or `DocumentReplacement`). Accepted persistent edits, Undo/Redo and replacements notify; selection, pan/zoom, scope navigation, validation alone and transient visibility do not.
- `OperationFailed`: `EventCallback<BpmnModelerOperationFailedEventArgs>`; exposes `Operation`, `Status` and `ImmutableArray<Diagnostic> Diagnostics`. These are bounded operation diagnostics, separate from model-validation Issues. Expected cancellation does not emit a failure callback. Context-edit rejections use the editor's interaction diagnostics, not an additional facade callback kind.

Callback **invocation** is ordered. Arbitrary asynchronous consumer callbacks may complete in a different order and may await subsequent modeler operations. Callback exceptions do not roll back committed changes. Track DocumentId together with revision: replacing a document can legitimately lower the revision number. Disposal and retired sessions suppress stale notifications.

## Editor document API and host operations (A3)

**Public API migration from 0.1.9:** native Import/Export and Publish facade methods were removed;
application-level Import/Export/Validate/Publish controls and validation reporting now belong to the host.
Add the Integration PackageReference above and move file selection, downloads and operation feedback to your host.
Do not replace removed methods with editor internals or source/DLL references.

| Previous integration | A3 host responsibility |
| --- | --- |
| `ImportNativeDocumentAsync` | Deserialize bytes, check native version with the migrator, call `SetDocumentAsync` with saved-routing verification. |
| `ExportNativeDocumentAsync` | Capture with `GetDocumentAsync`, serialize, download, acknowledge the matching native save checkpoint only after successful storage. |
| Editor Validate / Issues | Explicitly call `BpmnDocumentValidator` and present its report; no automatic validation during Import/Export. |
| `PublishAsync` | Capture and validate the document, capture its matching immutable presentation, invoke `PublishingPackageGenerator`, download the ZIP. |
| BPMN XML exchange | Use `ImportAsync` with the existing routing/geometry preparer and atomic installation; use code-defined `Export` and download `document.xml`. Report conversion losses separately from operation failures. |

`BpmnDocumentSerializer` and `BpmnDocumentMigrator` are static services. Create `BpmnDocumentValidator` and
`PublishingPackageGenerator` as host-owned services. Native JSON is the full-fidelity persistence format;
BPMN XML is a potentially lossy interchange format and must not acknowledge a native save checkpoint.
See the source Demo below for composition, technical rejection handling and validation-state invalidation.

The canonical document remains `DocumentSnapshot` from `Inceptus.DocumentEngine.Contracts.Documents`.
The reusable editor implements `IBpmnDocumentEditor`. Its document API is:

```csharp
BpmnModelerDocumentResult GetDocument();
ValueTask<BpmnModelerDocumentResult> GetDocumentAsync(CancellationToken cancellationToken = default);
ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(
    DocumentSnapshot? document, CancellationToken cancellationToken = default);
```

The asynchronous capture waits for accepted mutations. Neither capture method serializes, validates or creates files.
`SetDocumentAsync` installs a prepared snapshot atomically in a fresh session with fresh History and editor state.
It preserves supplied identities; failure or cancellation retains the previous usable editor. Editing commands and Undo/Redo
continue through the existing session. `CaptureDocumentSnapshot` and `LoadDocumentAsync` remain compatibility aliases;
`NewDocumentAsync` remains an editor lifecycle operation.

Inspect `Succeeded`, `Status`, `Snapshot` and `Diagnostics` on document results. Status is one of
`Succeeded`, `Rejected`, `Unavailable`, `Failed`, `Cancelled`. The editor no longer offers
`ImportNativeDocumentAsync`, `ExportNativeDocumentAsync` or `PublishAsync`, or renders Import/Export/Validate/Publish controls.
Immutable file-result and save-checkpoint contracts remain available to hosts.

The internal demo's `BpmnDemoHost` and `DemoDocumentOperations` are the reference host implementation:

- Import: host reads bytes, `BpmnDocumentSerializer.Deserialize`, `BpmnDocumentMigrator.Migrate`,
  then `SetDocumentAsync` with the existing atomic attachment/integrity checks.
- Export: `GetDocumentAsync`, serializer, host download, then acknowledge the issued save checkpoint.
- Validate: capture the document and immutable view context, run the integration validator, and display the report in the host.
- Publish: capture the document, validate, capture immutable presentation data, generate the package, and download it in the host.

Import and Export do not invoke `BpmnDocumentValidator` or run model-quality rules. Technical format, compatibility,
reconstruction and routing-integrity failures can still block transfer. A document with model-quality errors or warnings
can be transferred; the user chooses when to Validate. The package builder retains its existing publication eligibility checks.
Validation does not change the document.
Rules remain in the notation packages, composed by the integration validator; the editor holds no document-validation report.

`CaptureEditorContext` and `ViewChanged` let a host track attachment, scope and edit availability without a live runtime handle.
`SelectVisualAsync` provides guarded issue-target selection. `CapturePresentationAsync` supplies immutable active-view data;
`UpdatePublicationAsync` edits publication metadata through the existing command/history path. Save checkpoint issuance and
acknowledgement track externally completed storage without performing it.

Native JSON remains **Inceptus.Document, formatVersion 2**. The migrator accepts v2 unchanged and explicitly rejects
unsupported versions; no legacy migrations exist. The optional `BpmnModelerDocumentLoadOptions` overload preserves saved-routing
verification and opt-in incompatible-v2 recovery during attachment. This requires the editor's renderer metrics and routing
configuration and is an internal attachment precondition, not schema migration. Recovered content remains unsaved.

Start/End Event captions now use `OutsideBelow` placement. Older v2 documents with
saved `InsideCentered` caption geometry can fail strict attachment with
`INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE`. To recover deliberately, call
`SetDocumentAsync` with `VerifySavedRouting = true` and
`ReprepareIncompatibleRouting = true`, then save the recovered native document
and verify a subsequent strict load with the same rendering configuration.
Recovery regenerates incompatible routing; no automatic recovery or silent
schema migration is performed.

`BpmnStandardConverter` converts the supported interchange subset of **BPMN 2.0.2**. Native schema version and BPMN
standard version remain separate concepts. Conversion is deliberately lossy; it is not executable-process certification.

```csharp
var imported = BpmnStandardConverter.Import(xml);
// To retain DI paths, supply the host's existing routing/geometry preparer:
var prepared = await BpmnStandardConverter.ImportAsync(xml, routingPreparer, cancellationToken);
var exported = BpmnStandardConverter.Export(document);
// Check Succeeded before using Document / Xml. Display or persist ConversionLog in the host.
```

Each result contains `Document` or `Xml`, `Diagnostics`, `ConversionLog`, `Succeeded` and `HasLosses`.
`ConversionLog` contains only conversion losses: `Omitted` native information on export and `Ignored` BPMN information
on import, with a stable code, message, source identity, property and optional target identity. Successful mappings are
not logged. `Diagnostics` separately contains technical conversion errors/advisories; it does not duplicate losses or
include model-validation findings. Failure returns neither a partial Document nor partial XML.

| Mapping | Behavior |
| --- | --- |
| Process roots / nested scopes | Top-level processes and collaborations; owned scopes become embedded SubProcesses. No executable behavior is claimed. |
| Events | None Start/End; intermediate Message/Signal catch/throw and Timer catch; Timer/Message/Signal Boundary Events with attachment and interruption flag. |
| Tasks / gateways | Task, User, Manual, Service, Send, Receive; Exclusive, Inclusive, Parallel, Event-based gateways. |
| Flows | Sequence and Message Flows, endpoint IDs, names and documentation; existing A2 endpoint/ownership rules apply. |
| Participants / Pools / Lanes | Standalone A2 Pool becomes a Participant/process. Pools assigned to one A2 Participant become lanes of its process. Embedded SubProcess Pools become lanes. Explicit membership is authoritative; legacy process references do not infer membership. |
| IDs / text | Valid XML IDs preserved; other native IDs encoded collision-free with diagnostics. Names and plain descriptions map to name/documentation. |
| DI | Shape bounds, saved caption bounds, explicit label positions and ordered saved connector waypoints. Existing projection supplies Pool/Participant envelopes. |

Import uses a fresh revision and deterministic native defaults. Unnamed nodes requiring a native display name use their ID,
with a diagnostic. Script/Business Rule Tasks and Call Activities retain their abstract Task/flow structure while specialized
behavior is logged as ignored. Transactions and ad-hoc SubProcesses retain their embedded graph as ordinary SubProcesses.
Triggered Start/End events retain their base event; unsupported definitions are logged. For an Intermediate/Boundary event
with several definitions, the first supported definition in XML order supplies its native kind; all other definitions are logged.
Timer text is retained; on export only recognizable ISO date/duration/repeating-interval literals are classified. Untyped custom
expressions are omitted with diagnostics. No Inceptus XML extensions are emitted.

Unsupported BPMN data objects/stores/associations, execution expressions, resources, messages/signals payload definitions,
choreography/conversation content, nested lane hierarchy, extra diagram representations, foreign extensions and attributes are
ignored and logged. Unmapped process information has no native process-object property bag and is logged as ignored; compatible
defaults and neutral interchange metadata are accepted by the import profile below. Multiple documentation
blocks become one plain-text description. Black-box Participants use empty A2 Pools; direct Participant endpoints use the existing
Participant header plus an assigned Pool. The absence of a process/black-box marker is not preserved on subsequent export.

Native Code/ElementNumber, publishing metadata other than the exported definitions name, revision, custom/profile properties,
unmapped semantic kinds, anchor identities, pinning, routing mode, manual-route editing intent and routing proofs are omitted and
logged. Their visible geometry is converted where available. No layout, routing, file download or editor mutation runs on export.

Synchronous `Import` retains DI node bounds and endpoint attachment geometry, but explicitly logs omitted connector paths;
normal editor attachment prepares initial geometry. Use `ImportAsync` for DI path retention and for Message Flow diagrams.
It uses the supplied existing `IConnectorRoutingStatePreparer`: process layout first, then Message Flows, then supported Pool
dimension intents. DI paths become Manual routes. Native minimum geometry, boundary attachment, anchor and Pool policies can
normalize coordinates; changes are logged. Negative diagram origins are translated. Additional visuals use the first source-ordered
representation. Missing DI uses normal layout; there is no second routing engine. Pool/Lane frame placement and font measurement
are native presentation policies, so visual equality is not promised for arbitrary third-party layouts.

Import/export validate against the bundled, unmodified [OMG BPMN 2.0.2 schemas](https://www.omg.org/spec/BPMN/2.0.2/About-BPMN)
offline. Namespace URIs, not prefixes, identify BPMN and DI. Export uses the standard MODEL/BPMNDI/DD namespaces and a document-specific
`urn:inceptus:bpmn:` target namespace. Local qualified references are supported; external structural documents are not fetched.
DTD/entity expansion is disabled; XML is limited to 16,777,216 characters and 128 nested levels. Document reconstruction,
required ownership/reference checks and XML schema checks guard technical conversion integrity. Model validation does not
run at this boundary; import requires a coherent native result. Conversion does not change the validator's rules.

Blocking cases include malformed/schema-invalid XML, duplicate identities, missing/omitted required endpoints, incompatible attachment,
unresolved external structural references, ambiguous Participant/process sharing or Lane membership, cross-scope/Participant Sequence
Flows, nonrepresentable Message Flow ownership, and failed native geometry preparation. Unsupported isolated content is omitted instead.
The reference import produces **one main Inceptus diagram**, containing the Collaboration's Participants/Pools plus at most
one top-level process that no Participant references through `processRef`. Embedded SubProcesses retain their nested scopes.
More than one such unassigned process fails with `BPMN_IMPORT_MULTIPLE_UNASSIGNED_PROCESSES` (including names/IDs);
more than one Collaboration fails with `BPMN_IMPORT_MULTIPLE_COLLABORATIONS`. These are Inceptus compatibility limits,
not BPMN validity rules or model-quality validation. No process is selected silently and no artificial Pool is generated for this purpose.
Independent DI planes use the existing geometry preparation. If an explicitly positioned unassigned node still overlaps a Pool
from another plane, `BPMN_IMPORT_DI_COMPOSITION_UNSUPPORTED` rejects composition instead of inventing an arrangement.
Synchronous import checks supplied DI bounds; asynchronous import compares prepared nodes and Pool envelopes in scope coordinates.
The existing unassigned-region placement can resolve overlapping source planes, so a file rejected by the unprepared synchronous path can succeed through the normal asynchronous host import.
One Inceptus main diagram can still export as several standard processes: one per effective Participant, plus one for its
unassigned elements. Export mappings remain code-defined. Ordinary imported documents have no process/view selector;
SubProcess breadcrumbs remain available, and legacy native documents with peer root scopes retain their scope selector.
Round-trip tests compare supported types, names/descriptions, containment, membership, topology and representable DI; they do not use
XML bytes or whole `DocumentSnapshot.Equals()` as the interchange criterion.

The internal demo offers compact **Import** and **Export** format menus: **Inceptus JSON** (full fidelity) and
**BPMN 2.0.2** (potentially lossy interchange). BPMN import uses `.xml` (`.bpmn` remains an accepted alias), accepts files up to 16 MiB and uses
`ImportAsync` with the existing routing preparer, notation registrations and bundled browser font measurements.
The host passes the prepared snapshot to `SetDocumentAsync`; failed conversion or edits made during preparation
leave the current document intact. BPMN export converts the current snapshot and downloads UTF-8 `document.xml`
with MIME type `application/xml;charset=utf-8` without acknowledging a native save. Neither workflow invokes diagram validation.
The dismissible host feedback panel separates **Conversion losses** (Omitted/Ignored) from **Operation feedback**
(technical conversion/read/download failures and advisories). Explicit **Validate** alone creates the host validation report.
The host toolbar shows **Not validated** until a current report exists, then replaces it with clickable **Errors / Warnings / Info**
counts that open the existing dismissible Issues panel (including a zero-issue report). Editing or changing scope invalidates
the report and hides the counts again; it does not run validation automatically.
Publish retains its existing validation and publication behavior.

`Bpmn202ImportProfile.xml` is the authoritative declarative policy for interpreting BPMN 2.0.2 during import.
It lives in `src/Inceptus.Bpmn.Integration`, with namespace `urn:inceptus:bpmn:import-profile:1`, profile version `1`,
standard `BPMN`, and standard version `2.0.2`. **BPMN export is code-defined and is not configured by the import profile.**
The adjacent `Bpmn202ImportProfile.xsd` validates the vocabulary; the loader also rejects conflicting rules,
unresolved/unknown element QNames, unknown attributes and unsupported action/strategy combinations. Both files are embedded
in the integration assembly. `BpmnImportProfile.Default` loads and validates once into immutable typed `ImportRules`.
The `BpmnImportProfile` XML root directly contains `Fallback`, `Element` and `Transformation` rules; there are no
direction wrappers, Export bindings, or reserved Export configuration. Elements match namespace URI plus local name,
independently of the prefixes in imported XML. DTDs and external entities are prohibited, and profiles are limited to 1,048,576 characters.

The profile distinguishes actual source-information loss from compatible BPMN defaults. `exporter`/`exporterVersion`
are neutral metadata; non-executable/open processes, `processType=None`, horizontal Pool/Lane shapes, ordinary Activities,
ordinary SubProcesses and the default Event-Based Gateway properties do not generate losses. Non-default unsupported
execution properties retain the base structure and report only the lost property. Boolean `0`/`1` and equivalent whole-number
lexical values are compared by value. Plain documentation text is mapped; other text formats retain text and report formatting
loss. Gateway direction is accepted when it matches retained Sequence Flow topology (multiple incoming/outgoing flows determine
Converging/Diverging/Mixed); disagreement is reported. Missing Code/ElementNumber uses the existing deterministic native policy
without a routine warning. Required native name fallbacks and exceptional preparation advisories remain separate diagnostics.

`action` defines how imported source information is treated. The optional `Report` independently defines whether that
treatment represents a reportable loss of source information. No `Report` means no loss entry; a
`Report disposition="Ignored"` produces an import loss. There is no explicit `None` disposition and no `Omit` action.
The general conversion-result contract retains `Omitted` for code-defined export losses.

| Profile action | Treatment |
| --- | --- |
| `Map` | Use an existing converter binding to retain source information. |
| `AcceptDefault` | Accept the native/default assumption. |
| `AcceptMetadata` | Accept neutral standard interchange metadata. |
| `AcceptDerived` | Verify derivability using the code-defined topology strategy; use the optional report on disagreement. |
| `Normalize` | Use the existing native normalization. |
| `Degrade` | Use an existing supported base mapping. |
| `Ignore` | Ignore the unsupported or intentionally irrelevant information. |

Default policy examples:

| Source | Action | Report |
| --- | --- | --- |
| Pool/Lane `isHorizontal` missing or `true` | `AcceptDefault` | None |
| Pool/Lane `isHorizontal=false` | `Normalize` to horizontal | `Ignored`: vertical orientation changed |
| Process `isExecutable` missing or `false` | `AcceptDefault` | None |
| Process `isExecutable=true` | `Ignore` execution semantics, retain Process | `Ignored` |
| `definitions.exporter` / `exporterVersion` | `AcceptMetadata` | None |
| `dataObject` | `Ignore` | `Ignored`: unsupported data information |

For an alternate policy that deliberately considers a supported ignore binding irrelevant, `<Behavior action="Ignore" />`
is silent. Adding an `Ignored` Report to that same Behavior changes only loss reporting, not the treatment. Similarly,
`Normalize` without Report is silent and should be chosen only when the normalization is semantically equivalent from
the host's perspective. The shipped default still reports unsupported data and meaningful geometry changes.

Each `Element` selects namespace-qualified names via `qnames`; a list shares inherited Activity/Gateway rules without copying
them. Disjoint blocks for the same QName merge; conflicting duplicate attributes, behaviors or normalized values fail explicitly.
Attributes use `Missing`, explicit `Value` and `Any` rules. Only finite typed contexts/strategies are supported; the profile contains
no scripts, expressions or executable method names. Structural relationships and geometry formulas remain in code;
profiles cannot override required identity/reference bindings. Existing export mappings and geometry algorithms are unchanged.

Reports have stable codes and Element/Owner/Diagram/Document scopes. Pool-local coordinate normalization is reported once per
owning Pool with its name/identity, not once per child. Deduplication uses code/scope/identity/property, not message text. Other
contextual rules may aggregate by owner, diagram or document. Catch-all reports stay per element/property; broader aggregation
without a contextual binding is rejected explicitly. Distinct anonymous XML losses keep their source locations. Other
unclassified source attributes/elements still use an explicit `Ignore` fallback; XML namespace/schema-location mechanics are
excluded. Unclassified execution expressions, extension contents, nested lane hierarchy and additional DI semantics still require
future explicit classification; they are not silently declared compatible. Existing geometry/attachment/path transformations
are detected by the established converter checks; their optional reports are defined by import `Transformation` rules.
The refinement moved the existing negative-origin, boundary-position, connector-endpoint and connector-path reports into
these rules without changing their conditions, codes, messages or geometry algorithms.

Hosts/tests may load an alternate immutable profile without changing converter internals:

```csharp
var profile = BpmnImportProfile.Load(xmlProfileText); // or Load(stream), leaves stream open
var result = await BpmnStandardConverter.ImportAsync(xml, routingPreparer, profile, cancellationToken);
var imported = BpmnStandardConverter.Import(xml, profile); // synchronous, without DI routing preparation
var exported = BpmnStandardConverter.Export(document); // no profile dependency or overload
```

Invalid configuration throws `FormatException` before conversion. Alternate profiles can change supported classifications,
report codes/messages and aggregation; they cannot introduce new semantic construction or geometry algorithms. The profile does
not perform model validation. Parsing, reference/attachment integrity, A2 ownership, semantic construction and routing preparation
remain in converter code. `ConversionLog` still contains only `Ignored`/`Omitted` losses; technical failures remain in `Diagnostics`,
and explicit host **Validate** independently owns model-quality findings.

The 0.1.9 native v2 reader also accepts optional connector-anchor
`normalizedPosition` and visual-model `documentRoutingStates` fields. They are
written only when used; absent fields retain the previous defaults. Older v2
readers reject these new fields, so the unchanged format number does not imply
backward-reader compatibility for documents using them. No v1 migration is included.

## Publish a standalone presentation

`PublishingPackageGenerator.Generate(document, presentation)` creates the existing self-contained ZIP from a canonical document and matching immutable presentation capture. The capture preserves active scope and transient visibility, which are intentionally absent from the document. The generator does not need an editor instance, perform downloads or edit metadata. The reference host's Publication dialog can save changed valid metadata through one persistent editor command **before** generating the artifact; a later publication rejection does not undo that save.

Document-level Collaboration publication remains unsupported. Process views may
include valid Message Flows between different effective Participants. Receive
Tasks cannot send and Send Tasks cannot receive. Pool/Participant endpoints and
incoming Boundary Message Event delivery are graphical-only for animation and
return authoring warnings. Invalid canonical endpoints still prevent publication.
Communication links never become executable Sequence Flow.

The existing two-argument `Generate` overload remains available and uses Pools
OFF and Fit Entire. The options overload controls each generated artifact:

```csharp
var options = new BpmnPublishingOptions
{
    IncludePools = true,
    InitialViewMode = BpmnPublishingInitialViewMode.FitEntireDiagram,
};
var result = new PublishingPackageGenerator().Generate(document, presentation, options);
// Inspect result.Succeeded and result.Diagnostics before downloading result.Package.
```

For `BpmnPublishingInitialViewMode.CurrentEditorView`, supply an `EditorView` of
type `BpmnPublishingEditorView(DocumentId, Revision, ScopeId, VisibleRegion)`.
Obtain the document-coordinate `VisibleRegion` from the live
`CaptureEditorContext().Presentation.EditorState.Viewport.VisibleDocumentRegion`,
with the matching document/revision and active `ProcessScopePresentationContext`.
Require a ready editor and nonempty surface/region. A detached publishing Scene
is not the editor viewport authority. Guard the attachment version, document,
scope, viewport, surface and visibility through asynchronous capture and reject
stale or unavailable context; do not silently fall back to Fit Entire.
If saving publication metadata advances revision, accept only that expected
metadata command and recheck the same attachment/view against the returned
snapshot before generating. The reference dialog defaults to Current Editor view;
hosts can offer both modes independently of Include Pools. Changing either option
does not persist document data or enter History.

The artifact remains **Inceptus.PublishedProcess, formatVersion 1**, with these entries:

```text
index.html
process.json
process.data.js
inceptus.publish.js
styles.css
```

`process.data.js` contains the same PublishedProcess object as `process.json`. Classic scripts and static bootstrap data allow static HTTP hosting and direct-file startup without fetching JSON at runtime. Keep the generated files together with their matching viewer. The standalone output needs no authoring host, source checkout, Blazor, .NET or WASM runtime. Browser/security policy may restrict local `file://` access.

Publication metadata and element descriptions are captured as data. Descriptions are not displayed by the viewer. Node geometry and connector routes are frozen. Pool graphics are omitted by default; `IncludePools` can include representable captured Pool/Lane graphics without including the Unassigned region. Initial View changes framing only: existing API calls fit the complete diagram, while the internal Publish dialog defaults to the current Editor view. Diagram editing is unavailable; pointer drag and wheel/touchpad pan, and +/- controls zoom the model.

Initial framing adapts to the viewer viewport. After the user pans or zooms,
resizing preserves that manual view. Include Pools retains captured boundaries
and headers without reconstructing Pool geometry or changing token execution.

Publication requires **one or more Start-role nodes** in the active scope.
Reachable paths from every Start are included without cloning shared identities;
existing node and connector validation remains authoritative. With multiple Starts,
the user selects a Start in the diagram; the toolbar does not choose one or start
all of them. Repeated activations create independent tokens sharing gateway queues.

The generated execution metadata describes supported message routes, receiver
Activities and attached boundary triggers for the embedded runtime:

- Ordinary Activities process each Sequence Flow activation for 2 seconds before
  producing one ordinary output. A visible SubProcess behaves as an Activity;
  the viewer does not execute its nested contents.
- Message Throw sends on each supported outgoing Message Flow and continues its
  Sequence Flow when present. Each Activity completion sends on every supported
  outgoing Message Flow alongside its ordinary output. Blue diamonds follow the
  complete frozen route, including Manual bends; delivery occurs at the receiver.
  Message Catch immediately continues for each arriving message without enabling.
- A message-receiving Activity is enabled once by Sequence Flow; enabling alone
  produces no output. Messages received beforehand remain queued. An enabled idle
  receiver takes all pending messages into a fixed batch: N messages produce N
  ordinary outputs and N messages on each outgoing Message Flow after 2 seconds.
  Arrivals during processing form a later batch. Enablement survives successful
  output until reset or an interrupting boundary; later batches need no new token.
- An eligible empty receiver waiting cycle can fire Boundary Timer once, using a
  supported duration clamped to 1–5 seconds or a 3-second presentation fallback.
  Message arrival cancels its timer. Boundary Signal fires automatically once,
  without an external signal, and takes priority over Timer. `CancelActivity=true`
  disables the receiver while retaining pending messages; false keeps it enabled.
  Ordinary Activities without incoming Message Flow have no idle fallback cycle.
- Successful Activity output disables every attached boundary for that cycle,
  cancels timers and produces no automatic Boundary Message notification or later
  idle Signal. Idle state alone does not rearm boundaries. A fresh explicit
  Sequence Flow activation can reopen an eligible receiver waiting cycle.
- Exclusive joins pass each incoming token through immediately. Parallel Gateways
  consume one token from every input and emit one on every output. Nonparallel
  splits choose one random output without evaluating conditions; other supported
  nonparallel joins use simplified pairwise merging of distinct inputs. Message
  Flows never contribute gateway inputs.

Orange Sequence Flow tokens and blue messages use frozen path length at 300
diagram units per second, with a 150 ms minimum for nonzero routes. Zero-length
continuations are immediate. Logical timestamps determine ordering independently
of rendering frequency; a message arriving at the timer deadline wins. Runtime
reset clears execution state without changing input data. Bounded token and
iteration guards protect the viewer from runaway diagrams.

The viewer uses a custom presentation animation model. It has no message payload,
correlation engine, broker or external transport and does not implement complete
BPMN execution semantics or the future SimulationEngine. Regenerate old ZIPs to
obtain this runtime: each archive contains its own immutable data and viewer.

Host Publish failures and warnings remain visible outside the closed Publication dialog, with returned reasons and affected identities when available. Successful retry or session replacement clears obsolete feedback. These diagnostics belong to the editor and are not copied into the published model or shown by its viewer. Feedback is transient and does not enter Document History or persistent Issues.

## Advanced and implementation-facing surfaces

Lower-layer APIs support deliberate headless or browser/session composition, with their own ownership and extension contracts. The modeler's RCL remains the ordinary composition authority. Historical `BpmnPluginRegistration` milestone selectors are advanced historical compositions, not interchangeable support levels; the ordinary RCL currently uses `N100` and adds Organizational independently.

`IBpmnModelerStartupDocumentProvider.GetInitialDocumentAsync(CancellationToken cancellationToken = default)` returns `ValueTask<Inceptus.DocumentEngine.Runtime.Documents.Document>`. This advanced startup-only seam transfers ownership of a **fresh Document per component**; it is not a live model handle, replacement callback or persistence service. Prefer `InitialDocument` for ordinary startup.

The publicly generated `DocumentCanvas` and `ToolboxPanel` components are implementation-facing. Their visibility does not make them supported alternatives to `InceptusBpmnModeler`. The standalone `PublishedTokenRuntime.activateStart(startId, atMs)` method and frozen `startNodeIds` observation are advanced runtime integration surfaces; use the artifact's matching runtime. Viewer gesture helpers, `startAt` and control wiring are implementation-facing, not ordinary modeler APIs or a separate stable JavaScript SDK.

## Support limits and release identity

Acceptance covers the tested standalone Blazor WebAssembly host and recorded browser conditions. It does not establish Interactive Server support, every browser/device, BPMN XML interoperability, arbitrary cross-version API compatibility or production workflow execution. Native Inceptus.Document and standalone PublishedProcess are distinct formats.

An ID/version identifies immutable distributed package bytes. Recover an existing release only from retained hash-matching artifacts. Rebuilding the same source version does not recreate release identity; unrecoverable bytes require a deliberate new candidate/version. Candidate promotion and external publication are separate decisions.

The public source repository contains deliberate release snapshots. Public packages must be built from the corresponding public commit, which supplies repository commit metadata and SDK-native Source Link mappings. Version 0.1.3 prepares portable `.snupkg` symbols alongside each package. Source stepping requires the matching published symbols and publicly retrievable source; preparation alone does not establish nuget.org availability. Local application breakpoints remain distinct from stepping into packaged implementation.

First-party code is licensed under [MIT](https://github.com/INCEPTUS-LABS/DocumentEngine/blob/main/LICENSE), Copyright (c) 2026 Inceptus Robert Prokopczuk. Redistributed third-party material retains its own terms; see [third-party notices](https://github.com/INCEPTUS-LABS/DocumentEngine/blob/main/THIRD-PARTY-NOTICES.md), including the separately bundled DejaVu font license. See [release notes](https://github.com/INCEPTUS-LABS/DocumentEngine/blob/main/RELEASE-NOTES.md). These documents are also included in each primary package.
