# CP6 CAD remote Worker protocol v1

The Design API never loads a vendor CAD SDK and never launches a CAD process.
It streams only the raw CAD source to a separately deployed Worker over mTLS;
Tenant, Site, user, model, database, mapping, object-storage, and CP6 credential
identities are not part of this protocol.

## Request

`POST {approved-base-uri}/v1/conversions`

- Content type: `application/vnd.cp6.space.cad-source`
- Body: raw DWG or DXF bytes, limited to 200 MiB.
- Required single-value headers:
  - `X-CP6-Cad-Schema: 1`
  - `X-CP6-Cad-Attempt: {uuid}`
  - `X-CP6-Cad-Source-Sha256: {lowercase sha256}`
  - `X-CP6-Cad-Source-Format: Dwg|Dxf`
  - `X-CP6-Cad-Provider-Key: {certified key}`
  - `X-CP6-Cad-Provider-Version: {certified exact version}`

The Worker must stage the complete source on its isolated ephemeral volume and
verify the source SHA-256 before starting any converter. It must invoke the
configured `ICadConverter` only through `SpaceCadConverterContractRunner`.

## Response

Success is HTTP 200 with content type
`application/vnd.cp6.space.cad-worker-response+json` and a
`SpaceCadWorkerConversionResponseV1`. The response repeats the attempt, source,
format, Provider key/version, includes the CAD IR package, and binds the package
to its canonical SHA-256. No raw CAD, filesystem path, external-reference path,
credential, or business record may be returned.

CP6 validates the response identity, package hash, source/format and converter
identity. Coordinate preparation, frozen Mapping Profile replay, semantic
parsing, diagnostics, and PreviewSet generation all execute inside CP6. A
Worker therefore cannot choose a mapping or write a Draft.

## Deployment gate

Runtime registration is absent by default. Enabling
`Space:Cad:RemoteWorker` requires:

- an HTTPS base URI, mutually authenticated client certificate, and server
  certificate SHA-256 pin;
- a deployment-owned approval Manifest with an externally supplied exact hash;
- the same Provider key/version, deployment mode, data boundary and format
  support as the Site certification;
- qualification score at least 80 on the frozen authorized dataset/environment;
- licensing, security, region, deletion/retention, Worker identity and
  certificate evidence;
- asserted and externally verified no-egress, no-business-credential,
  source-hash-before-conversion, contract-runner, raw-deletion and artifact-only
  controls.

An expired, changed, incomplete, example, fixture or placeholder Manifest makes
startup fail. These repository controls do not themselves constitute supplier,
customer, Site, licensing or GA acceptance.
