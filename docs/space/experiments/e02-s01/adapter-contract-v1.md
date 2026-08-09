# E02-S01 CAD adapter observation contract v1

Status: experiment-only
Schema version: `1`

The CAD experiment runner treats every SDK or service integration as an
out-of-process adapter. The adapter must accept:

```text
inspect --input <absolute-path> --output <absolute-json-path>
        --candidate-version <version>
```

It must exit `0` only after atomically producing a complete JSON observation.
The runner validates schema version, candidate version, input SHA-256, process
exit, timeout, and output presence. Geometry accuracy and expected issue
matching remain separate evaluators because a low-level entity inventory cannot
prove a correct Space logical model.

## Observation

```json
{
  "schemaVersion": 1,
  "candidateVersion": "vendor-sdk-version",
  "sourceSha256": "64 lowercase hex",
  "format": "DXF|DWG",
  "cadVersion": "AC1015",
  "unit": "Millimeter",
  "coordinateSystem": "FloorLocal-ZUp",
  "entityCount": 7,
  "handleCount": 7,
  "duplicateHandleCount": 0,
  "entityTypeCounts": {
    "LINE": 2
  },
  "layerCounts": {
    "WALL": 1
  },
  "unsupportedEntityCounts": {},
  "issues": []
}
```

Rules:

- `sourceSha256` must be calculated from the exact input bytes.
- `entityCount` is the top-level model-space entity count used by the adapter.
  The adapter must document any paperspace, block-definition, XRef, or proxy
  exclusions.
- A usable `Handle`/source identifier counts toward `handleCount`; synthesized
  array indexes do not.
- Unknown unit or coordinate system must remain `null` and produce a Blocking
  issue in the downstream evaluator. The adapter must not guess.
- Unknown or unsupported entities must be counted; silently dropping them is a
  failed observation.
- Candidate adapters must not make external network calls unless their
  candidate definition explicitly declares a controlled conversion service.

## Evidence retained by the runner

For each attempt, the runner writes `run-evidence.json` and preserves:

- candidate ID/version and source SHA;
- iteration, start time, elapsed milliseconds, and peak working set;
- outcome, exit code, stdout, and stderr;
- observation path and SHA when present.

The aggregate report also records OS, process architecture, .NET runtime,
timeout, and run count. Vendor logs and engine IDs should be added to the
observation `issues` or an adjacent vendor evidence file when the service
exposes them.
