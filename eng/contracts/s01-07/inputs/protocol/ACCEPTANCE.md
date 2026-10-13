# UA-20261002-C-QA-IPQC-MD01

Date: 2026-10-02 UTC  
Target: QA-IPQC-01 | 过程与最终检验  
Task: C-QA-IPQC-MD-SPEC-01  
Accepted revision: Recovery-R4 Systemic-R4  
Decision: FORMAL_DESIGN_ACCEPTANCE

## Accepted unit

- ZIP bytes: 16190235
- ZIP SHA-256: 5a3adbef8a385c0f692890c62a824365cfee259edd0491fdabc9fc9ff52ed47c
- Manifest: 46 members / SHA-256 4fc2ce659a20d9685f883128d9b72ac2ee1e5ab59b743e8ff63038f4f4a6a48a
- Main bytes/lines: 37378 / 502
- Main SHA-256: c82ef3d5833b823cc562606af1a8bd8a68653416790bfc09c895df3a9a8c511b
- Cumulative static conclusion SHA-256: 917d5d43f68925feb3a5859624b88b634e0f23d9ac1cd11264e64278874c68da
- Final 17-column bounded review SHA-256: b8ef9b426448c62a1e8dd25fa85d9c0cda4e9b66560f1bccd6ff2404028ae6e4

The accepted design unit covers exactly the five original QA-IPQC specifications and the complete frozen canonical/API/relational/frontend/fixture/source/trace package.

## Decision basis

1. C1 actual adoption of the exact frozen candidate's 15 principal specification artifacts
2. independent cumulative `PASS_STATIC_DESIGN_CUMULATIVE`
3. bounded R4 acceptance of the final 17-column composite-fact gate and direct propagation
4. O1 `FINAL_REVIEW_PASS / READY_FOR_DESIGN_ACCEPTANCE` based on the accurately bounded review record
5. root formal design acceptance at 2026-10-02T09:46Z, minute precision

## Acceptance boundary

PLM-VER and PLM-DOC remain RequiredFenced. Main Quality owns professional inspection, measurement and Quality decision facts; MES-EXEC owns operation source facts; MES-OUTPUT owns output facts; QA-POL owns policy/plan facts; PLM retains engineering-evaluation and controlled-document ownership. No runtime protocol, production writer or Owner adoption is inferred from the design.

Frontend routes and actions are accepted only as DESIGN_CODEPOINT_CONTRACT_ONLY. The historical R3 title inside the frozen INDEX remains unchanged and is corrected only by the external cover/corrigendum.

## Registry effect

- Original stage: 10
- Final accepted stage: 100
- Review event: +70 Main / +70 All / +0 Shared
- Acceptance event: +20 Main / +20 All / +0 Shared
- Net: +90 Main / +90 All / +0 Shared
- Accepted main-chain target count: +1
- Delta replay allowed: false
- No C successor assignment is included

## Runtime boundary

- all 50 business AC: NOT_RUN
- frozen scripts: not executed for archival
- product DB, SQL/TypeScript compiler, frontend build, CI and deployment: NOT_RUN
- Owner/runtime adoption: UNPROVEN
- ImplementationAuthorized: false
- Business code modified: false

This acceptance is product-design acceptance only.
