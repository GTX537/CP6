import http from '../http'

type Action = 'REPLAY_ORIGINAL_REQUEST' | 'REDELIVER_ORIGINAL_RESULT' | 'RESUME_POSTCOMMIT_BRIDGE'
type NativeRef = { ownerId: string; nativeType: string; nativeId: string }
type Digest = { domain: string; codecVersion: string; value: string }
type Evidence = { allowedAction: Action; nativeQueueRef: NativeRef; expectedDigest: Digest }

export interface IntegrationFailure {
  failureRef: string
  ownerId: string
  nativeType: string
  nativeId: string | null
  nativeVersion: string | null
  occurredAtUtc: string | null
  originalMessageRef?: string
  originalRequestRef?: { requestId: string; requestVersion: string }
  resultVersion?: string
  attemptCount: number
  safeCode: string
  deliveryState: string
  ownerOutcome: string
  evidenceState: string
  replayEvidence?: Evidence
}

export interface IntegrationPage {
  items: IntegrationFailure[]
  partial: boolean
  sourceSet: string[]
  unavailableOwners: string[]
  snapshotAt: string
  cursor: string | null
}

export interface IntegrationEligibility {
  status: string
  safeCode: string | null
  expiresAtUtc: string
  eligibilityRef: string | null
  replayOperationId: string | null
  replayEvidence: Evidence | null
  nativeVersion: string | null
}

export interface IntegrationReplayRequest extends Evidence {
  replayOperationId: string
  expectedNativeVersion: string
  reasonCode: 'dependency-recovered' | 'contract-verified'
  eligibilityRef: string
  inputContractVersion: 'ReplayRequest.v1'
}

export interface IntegrationReceipt {
  replayOperationId: string
  status: string
  safeCode: string | null
  acceptedAtUtc: string
  ownerOutcome: string
}

const root = '/integration-ops/v1'
const failurePath = (ref: string) => `${root}/failures/${encodeURIComponent(ref)}`
export const integrationOpsApi = {
  list(cursor?: string) {
    return http.get<unknown, IntegrationPage>(root + '/failures', { params: { cursor } })
  },
  detail(ref: string) {
    return http.get<unknown, IntegrationFailure>(failurePath(ref))
  },
  eligibility(ref: string) {
    return http.post<unknown, IntegrationEligibility>(failurePath(ref) + '/eligibility')
  },
  schedule(ref: string, request: IntegrationReplayRequest) {
    return http.post<unknown, IntegrationReceipt>(failurePath(ref) + '/replays', request)
  },
  original(operation: string) {
    return http.get<unknown, { status: string; receipt: IntegrationReceipt | null; currentExecution?: string }>(
      root + '/replays/' + encodeURIComponent(operation),
    )
  },
}
