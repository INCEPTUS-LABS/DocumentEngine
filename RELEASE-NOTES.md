# Release notes

## 0.1.8

- Makes explicit Manual connector route-point edits participate correctly in the
  single global EditingSession History. Add, move and remove route-point
  operations, together with routing-mode changes, Undo/Redo exactly while
  derived Automatic routing work remains non-authoring and does not create
  separate History entries.
- Stabilizes context-menu presentation for its open lifetime. Available actions
  are captured when the menu opens and no longer appear or disappear because of
  transient pointer/session state; execution still validates the current state.
- Improves Gateway external-label resizing with a 20 x 20 logical-unit minimum,
  exact anchoring across all resize directions and live orange-boundary feedback
  that follows the clamped transient preview without a stale outline.
- Allows connector Properties to apply Name and Routing type together in one
  atomic Apply. The combined edit produces one revision, one History entry and
  one committed event; invalid or stale combinations reject atomically, and
  Undo/Redo restores the exact previous authored Manual route when applicable.
- Optimizes connector-label movement through bounded previews and Scene-only
  commits. Steady drag no longer performs full Scene composition, contributor
  execution, unrelated caption measurement or full-content uploads, while the
  connector route remains read-only input and its identities, anchors, order
  and saved/authored/calculated paths remain unchanged.
- Extends the existing contributor dependency contract with conservative,
  opt-in connector-label-move dependency metadata so bounded reuse is used only
  for audited invariant contributors; Unknown or dependent contributors retain
  the full-composition fallback.
- Retains native document format v2, PublishedProcess format v1, the coordinated
  six-package family targeting net10.0, and the existing Source Link and
  portable-symbol release requirements. No new persistence format is introduced.

## 0.1.7

- Adds stable persisted connector routing. Complete connector paths, routing
  modes and deterministic per-scope order are retained in the Visual Model;
  valid Automatic routes are preserved and only affected routes are repaired.
- Adds explicit Automatic, Straight and Manual routing modes. Manual routing
  supports authored intermediate points without automatic obstacle detours.
- Improves Manual route editing with horizontal/vertical bridge decoration,
  transient dashed guidance for diagonal candidate segments, Ctrl orthogonal
  assist and magnetic H/V snapping with hysteresis.
- Adds compact Organizational Pool presentation together with authored
  Pool/Unassigned heights, one shared scope width and direct mouse edge
  resizing. Connector and connector-label overflow no longer sizes Pool frames.
- Improves pointer interaction performance through bounded presentation paths
  for placement, selection, node-label movement and Manual route-point movement,
  avoiding unnecessary full Scene composition and renderer uploads on supported
  steady interactions.
- Adds native document format v2 with persisted routing and spatial geometry,
  coherent Save/Open reproduction and strict validation of the saved geometry.
  Native v1 documents are intentionally not accepted by this release.
- Retains the coordinated six-package family targeting net10.0, portable
  symbols and Source Link/public-source provenance requirements.

## 0.1.6 candidate

- Prepares the coordinated six-package release checkpoint after A1.2.13. The published 0.1.5 identity remains immutable; Canvas2D requires Runtime exactly [0.1.6], and other family dependencies retain their minimum-version policy.
- Adds BPMN SequenceFlow names with automatic near-source branch labels, editable through Properties, with manual placement, reset and exact Undo/Redo.
- Includes dependency-safe pan reuse, renderer-resident frame caching, bounded Blazor presentation work and viewport culling, plus document-origin viewport normalization and stable boundary guides.
- Reduces orthogonal routing work and bounds activity drag-preview, hit-testing and selection work while preserving the existing Document, command, History and derived-processing authorities.
- Refreshes the canonical source Demo with named event-gateway branches alongside its activities, subprocess and boundary timer. No feature switch is required for the accepted fast paths.
- Retains net10.0, existing native/PublishedProcess formats, dependency baselines and portable symbols. These are local preparation artifacts; public source provenance and publication remain separate release steps. A1.2.14 is not included.

## 0.1.5 development

- Defaults the source Blazor host UI to neutral English while keeping data culture invariant; reusable modeler languages remain host-selected.
- Simplifies element Properties to Data and Parameters, with readonly Type. Technical identities and visual geometry remain in the model and are no longer shown in this form.
- Hides ordinary Properties for BPMN Events and elements without Data fields. Long-form fields such as Description use a wider, responsive dialog and a taller editor, while short fields stay compact.
- Closes Properties after successful Apply and retains failed drafts for review. Existing commands, History, document events and data formats remain authoritative.
- Starts the coordinated six-package 0.1.5 development line, including the P1.14 deterministic integration-event synchronization correction. Existing dependency rules and the public v0.1.4 release remain unchanged.

## 0.1.4 development

- Adds modeler UI localization for English, Polish, French, German and Spanish using standard .NET resources and `IStringLocalizer`. English is the neutral/default resource and final fallback; regional cultures use standard parent-culture fallback.
- Uses the host's `CurrentUICulture` and Blazor re-rendering without a public culture parameter or modeler language selector. Localization is presentation state; it does not change Document identity, revision, History, selection, viewport or editing-session ownership.
- Localizes toolbox labels, toolbar/status text, Properties labels, context actions, navigation fallbacks, New/Publication dialogs and presentation feedback. Canonical diagnostics and authored document values remain unchanged.
- Keeps serialization, native import/export and PublishedProcess contracts English-based and culture-invariant. There is no public data-format change; the standalone exported viewer is outside this localization phase.
- Advances all six development packages to 0.1.4, retaining Canvas2D's exact matching Runtime dependency and other existing dependency-range semantics. This phase does not publish packages or update package consumers.

## 0.1.3 candidate

- Prepares the first public-source release snapshot, with the approved GitHub RepositoryUrl and commit metadata derived from the public build checkout.
- Uses the pinned .NET SDK's built-in Source Link and portable `.snupkg` symbols for all six packages. Public source and symbol retrieval must be verified before release.
- Maps Release Razor-generated source directives before compilation, preserving generated EmbeddedSource and authored-source stepping without embedding checkout-specific paths. Debug retains the SDK's normal source generator and Hot Reload support.
- Aligns SDK Source Link roots with the existing compiler PathMap in ordinary local builds as well as CI builds.
- Adds public-source validation and a separate, manually dispatched, tag-gated NuGet Trusted Publishing workflow. Preparation does not publish packages or create a release tag.
- Corrects a session-shutdown lock-order inversion with in-flight document-change observation, preserving notification delivery and deterministic resource cleanup.
- Preserves the accepted 0.1.2 D1 diagnostic lifetime correction and existing multiple-Start Publishing behavior. No public API, native format or PublishedProcess format changes are introduced.
- Advances the aligned family to 0.1.3, including Canvas2D's exact Runtime [0.1.3] dependency. Other approved ranges remain unchanged; retained 0.1.0, 0.1.1 and 0.1.2 bytes remain immutable.

## 0.1.2 candidate

- Preserves rejected context-command feedback across presentation-only rebuilds of the same document, revision, scope and session, including the canvas resize caused by displaying the feedback itself.
- Keeps authoritative replacement, revision/scope changes, retirement/disposal and later interaction outcomes responsible for invalidating obsolete feedback.
- Adds permanent surface-rebuild and diagnostic-invalidation regressions. There is no public API, native document format or PublishedProcess format change; the existing multiple-Start Publishing extension remains intact.
- Advances the aligned six-package family to 0.1.2. Canvas2D retains its matching Runtime constraint; other dependency policies and approved release metadata are unchanged.

The retained 0.1.1 candidate was promoted locally but did not pass final browser D1 acceptance: its rejection message disappeared after an alert-induced canvas resize. Its archives and failure evidence remain unchanged. The 0.1.2 source correction requires a deliberate source commit before final packing, audit, local promotion and package-only browser acceptance; none is implied by these notes.

## 0.1.1 candidate

- Extends existing standalone Publishing to support multiple independent Start activation points, all start-rooted paths and shared downstream identities. PublishedProcess remains format version 1; artifacts include their matching viewer.
- Keeps Publish failure reasons visible after the Publication dialog closes, preserves returned diagnostics and affected identities, and clears obsolete feedback on retry or document replacement. Saving Publication metadata remains independent of later package-generation failure.
- Surfaces rejected context-command diagnostics through the editor's existing interaction feedback while preserving document atomicity, History and callback behavior.
- Adds consumer integration, lifecycle, hosting and API support documentation, packaged README content, first-party MIT licensing and the approved product URL.
- Defines support for the aligned six-package family. Canvas2D constrains Runtime to its matching version because of their internal History dependency; other dependency policies are unchanged.

The candidate targets net10.0. It does not add BPMN XML interoperability, a production process-execution engine or new hosting-model support. Source Link and package implementation source debugging remain deferred until a public source repository exists. Candidate preparation does not imply local-feed promotion or public NuGet publication. Retained 0.1.0 artifacts remain unchanged.
