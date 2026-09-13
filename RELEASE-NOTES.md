# Release notes

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
