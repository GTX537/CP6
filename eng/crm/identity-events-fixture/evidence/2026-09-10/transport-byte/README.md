# C02 local Dapr/Kafka byte and metadata probe

These original reports were produced on local Core base `7302f7dcc70be343de14d8825883512554b34647` plus the explicitly uncommitted transport changes. Each binds the executed API assembly hash. Environment records include the cached Docker image identities. No Actions, image build, SQL fixture or CRM management request was involved. These are generated-event transport observations, not full C02 or production acceptance.

| Attempt | Outcome | Interpretation |
| --- | --- | --- |
| 1 | Failure | Actual producer changed both full-event bytes and data hash; explicit raw publish preserved them. The original metadata comparison also supplied two conflicting key aliases, so it is not reusable as a header-spoof proof. |
| 2 | Failure | The corrected production publisher preserved exact bytes and data hash. The dual-alias metadata test produced a different actual Kafka key, exposing the flawed positive expectation. |
| 3 | Success, 3/3 | Actual producer preserved bytes/hash/routing. Case-insensitive reserved-header filtering preserved trusted metadata. An intentionally wrong actual broker key was rejected by the production delivery validator. |

The fix adds `rawPayload=true` to publish metadata for the already validated structured CloudEvent. The receiving subscription stays `rawPayload=false`. The component filter is `(?i)^(__(key|topic|partition|offset|timestamp)|pubsubname)$`; case-insensitivity is necessary because the app receives HTTP headers.

Dapr's [Kafka producer implementation](https://github.com/dapr/components-contrib/blob/v1.18.2/common/component/kafka/producer.go) treats `partitionKey` and `__key` as aliases before excluding headers. Its [consumer implementation](https://github.com/dapr/components-contrib/blob/v1.18.2/common/component/kafka/consumer.go) derives broker metadata and then adds permitted record headers. The probe therefore distinguishes actual broker-key mismatch from header spoofing. All three attempts' owned containers and networks were removed; private logs are retained outside Git.

Actual Core/CRM snapshot import, business events, transaction/crash replay, priority progress, two-tenant behavior and management-denial timings remain pending.
