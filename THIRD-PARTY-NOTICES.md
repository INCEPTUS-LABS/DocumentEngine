# Third-party notices

The repository-level MIT license applies to first-party DocumentEngine code. It does not replace licenses for third-party assets or dependencies.

## DejaVu Sans

`Inceptus.DocumentEngine.Bpmn.Blazor` redistributes `DejaVuSans-2.37.ttf` through its static web assets. Its accompanying `DejaVuSans-LICENSE.txt` preserves the applicable Bitstream/Arev notices and terms. Both files are in that package's `staticwebassets/fonts/` directory and are served under `_content/Inceptus.DocumentEngine.Bpmn.Blazor/fonts/` by normal static-web-asset integration.

The complete font license is retained separately beside the font, including in the source at `src/Inceptus.DocumentEngine.Bpmn.Blazor/wwwroot/fonts/DejaVuSans-LICENSE.txt`. Do not substitute the product MIT license for those terms.

## OMG BPMN schemas

`Inceptus.Bpmn.Integration` embeds the unmodified normative `BPMN20.xsd`, `Semantic.xsd`, `BPMNDI.xsd`,
`DI.xsd` and `DC.xsd` files published by the Object Management Group for BPMN 2.0.2.
Source: https://www.omg.org/spec/BPMN/20100501/ (individual schema names above), retrieved 2026-10-11.
The normative file index and specification notices are at https://www.omg.org/spec/BPMN/2.0.2/About-BPMN
and https://www.omg.org/spec/BPMN/2.0.2/PDF. Copyright belongs to OMG and the contributors identified there;
these third-party specification files are not relicensed under the repository's MIT license.
They are used for offline interchange validation; the implementation does not claim full BPMN conformance or certification.

## Package dependencies

NuGet dependencies retain their respective licenses. The generated nuspec dependency groups identify each package's dependencies and ranges. This notice is an index of bundled third-party material, not a claim that first-party licensing grants rights to all dependency material.
