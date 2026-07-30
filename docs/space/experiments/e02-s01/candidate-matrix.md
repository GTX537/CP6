# E02-S01 CAD candidate matrix

Evidence date: 2026-07-30
Decision state: provisional; hard-gate experiment incomplete

ADR-0001 weights remain: coverage 25, fidelity 20, performance/stability 15,
security/operations 15, license/TCO 15, support/exit 10. No candidate receives a
final numeric score until it has run the same licensed dataset and stress
protocol. Vendor documentation can qualify a trial; it cannot award experiment
points.

| Candidate | Official capability evidence | Platform and operations | License/TCO evidence | Current hard-gate result | Disposition |
|---|---|---|---|---|---|
| ODA Drawings SDK 27.6 | ODA states native DWG/DXF access, old through current DWG support, and full data access including xdata. | Official platform list includes Windows and Linux; malicious-file isolation, container ABI, cancellation, concurrency, 200MB, and 1M entities remain untested. | ODA membership table says Commercial has no Web/SaaS use, Sustaining and Founding do; current listed first-year prices are USD 3,000 / 7,500 / 37,500. Exact CP6 Worker, DR, non-production, and redistribution terms require a signed legal conclusion. | Documentation-qualified only. No SDK account, licensed run, DWG corpus, Linux run, or fidelity result. | **Primary licensed trial**; not yet the implementation selection. |
| Autodesk APS AutoCAD Automation | Autodesk states the cloud AutoCAD engine can run custom add-ins, scripts, and AutoLISP to process DWG. This is more suitable for a versioned CP6 CAD IR than viewer-only derivative output. | Controlled cloud dependency: outbound network, credential rotation, approved data region, retention, retry, cancellation, and engine lifecycle are mandatory. Autodesk documents engine deprecation/removal dates. | APS moved to Free/Paid tiers and tracked billing. Region, DPA, paid usage, and disaster-recovery cost need procurement confirmation. | Documentation-qualified only. No CP6 app bundle, credentials, regional proof, or sample run. | **Backup licensed trial**; not yet approved for production data. |
| Aspose.CAD for .NET 26.6.0 | Official docs list standalone .NET/Core DWG/DXF loading and broad format support. Local evaluation read entity type, unit, and Handle for L1-L4. | Embedded .NET process. L5 crashed 5/5 during load; L1-L4 lost all original code-8 layer names because the minimal Seed omits a layer table. Unlicensed stress reads were truncated to 100 entities, so capacity is unassessed. | Aspose says public web/SaaS distribution requires an appropriate OEM-style license; a temporary license is required for an unrestricted evaluation. | **Failed fidelity and stability gates** on the frozen Seeds. Capacity cannot be scored without a temporary license. | Rejected from primary/backup shortlist for E02-S01. |
| Autodesk RealDWG 2027 | Autodesk states native DWG/DXF read/write from Release 14 onward. | Current official requirements list Windows 11, Visual Studio 2026, and .NET 10. This fails the frozen Linux-container portability gate. | Evaluation/licensing is referred to an Autodesk partner; legal/TCO remains unknown. | Platform hard-gate failure for the preferred Worker topology. | Reject as primary; retain only as a Windows-worker contingency if architecture is explicitly changed. |
| Autodesk Model Derivative | Autodesk states 70+ formats, geometry/property extraction, and SVF/SVF2 viewer translation. | Cloud/region dependency; output is optimized for Viewer workflows. | Paid API usage and regional governance apply. | Does not by itself prove the stable Handle/source-reference and CP6 CAD IR contract. | Do not use as the E02 conversion backup; it may still serve a future viewer pipeline. |

ODA File Converter 27.1 is not a candidate or a corpus-generation dependency.
The official download page describes it as a GUI/command-line example but does
not publish the current argument contract. ODA's official FAQ also limits
non-member use of the free example to non-commercial applications. Local
signature verification and guarded CLI probes are recorded only as negative
tooling/license evidence; no generated asset is accepted into the corpus.

## Official sources

- [ODA Drawings SDK](https://www.opendesign.com/products/drawings)
- [ODA product descriptions](https://www.opendesign.com/faq/product-descriptions)
- [ODA membership and current published prices](https://www.opendesign.com/oda-membership)
- [ODA File Converter](https://www.opendesign.com/guestfiles/oda_file_converter)
- [ODA free-example usage boundary](https://www.opendesign.com/faq/question/what-are-oda-viewer-and-oda-file-converter)
- [Autodesk RealDWG API](https://forge.autodesk.com/developer/overview/realdwg-api)
- [Autodesk Automation APIs](https://aps.autodesk.com/automation-apis)
- [Autodesk AutoCAD engine lifecycle example](https://aps.autodesk.com/blog/end-autocad-2021-engine-new-autocad-2027-engine-released)
- [Autodesk Model Derivative conversions](https://aps.autodesk.com/model-derivative-api-2d-3d-conversions)
- [Autodesk regional behavior](https://aps.autodesk.com/blog/data-management-and-model-derivative-regions)
- [Autodesk APS business model evolution](https://aps.autodesk.com/blog/aps-business-model-evolution)
- [Aspose.CAD for .NET](https://products.aspose.com/cad/net/)
- [Aspose license types](https://purchase.aspose.com/policies/license-types)
- [Aspose.CAD licensing](https://docs.aspose.com/cad/net/licensing/)

Prices and product terms are web evidence captured on the evidence date, not a
quote or legal approval.

The 2026-07-30 review confirmed ODA release 27.6, the published SaaS tier
boundary and prices, APS rated billing, AutoCAD 2027 engine lifecycle,
Aspose's licensed-evaluation requirement and RealDWG 2027's Windows-only
system requirement. These documentation facts qualify trials but do not award
ADR score or replace legal approval.
