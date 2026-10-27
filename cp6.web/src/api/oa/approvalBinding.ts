import http from '../http'
export interface BindingRule { ruleId: string; priority: number; when: string; flowKey: string; flowDefVersionId: string | null }
export interface BindingWrite {
  commandId: string; bizType: string; subjectKind: string; selectionMode: 'EXACT_PINNED' | 'RESOLVE_PUBLISHED_ON_SUBMIT'
  flowKey: string; flowDefVersionId: string | null; effectiveFrom: string; effectiveTo: string | null
  ruleMode: 'EXCLUSIVE_MATCH' | 'ORDERED_FIRST_MATCH'; rules: BindingRule[]; detailRoute: string | null; remark: string
}
export interface BindingInterval { from: string; to: string | null; revisionId: string | null; state: string }
export interface BindingView extends BindingWrite {
  bindingId: string; revisionId: string; revision: number; state: string; owner: string; rowVersion: string
  createdBy: string; createdAt: string; timelineRevisionId: string; effectiveInterval: BindingInterval | null; scheduledIntervals: BindingInterval[]
}
export interface BindingOptions {
  businessTypes: { bizType: string; owner: string; subjectKinds: string[]; snapshotSchema: string }[]
  flowVersions: { flowKey: string; versionId: string; version: string; status: 'Published'; headEnabled: boolean }[]; asOf: string
}
export interface BindingIntent { bindingId: string | null; etag: string | null; operation: 'SAVE' | 'DISABLE'; command: BindingWrite | { commandId: string; reason: string } }
export interface BindingResult { commandId: string; state: string; result: BindingView; queryRef: string }
export interface BindingQuery { commandId: string; state: string; targetType: string; targetId: string; resultRef: { owner: string; id: string; version: string } | null; errorCode: string | null; recordedAt: string }
export interface BindingTimeline { timelineRevisionId: string; bindingId: string; timelineNo: string; previousTimelineRevisionId: string | null; publishedAt: string; publishedBy: string; intervals: BindingInterval[] }
const root = '/oa/app/v2', data = <T>(v: any): T => v?.data ?? v
export const approvalBindingApi = {
  capabilities: async (signal?: AbortSignal) => data<{ schema: string; bindingRead: boolean; bindingConfigure: boolean }>(await http.get(root + '/capabilities', { signal })),
  options: async (signal?: AbortSignal) => data<BindingOptions>(await http.get(root + '/binding-options', { signal })),
  list: async (cursor: string | null, signal?: AbortSignal) => data<{ items: BindingView[]; nextCursor: string | null }>(await http.get(root + '/bindings', { params: { cursor }, signal })),
  history: async (id: string, cursor: string | null, signal?: AbortSignal) => data<{ items: BindingView[]; nextCursor: string | null }>(await http.get(root + `/bindings/${encodeURIComponent(id)}/revisions`, { params: { cursor }, signal })),
  timeline: async (id: string, timeline: string, signal?: AbortSignal) => data<BindingTimeline>(await http.get(root + `/bindings/${encodeURIComponent(id)}/timelines/${encodeURIComponent(timeline)}`, { signal })),
  execute: async (intent: BindingIntent, signal?: AbortSignal) => data<BindingResult>(await http.post(root + (intent.bindingId ? `/bindings/${encodeURIComponent(intent.bindingId)}/${intent.operation === 'DISABLE' ? 'disable' : 'revisions'}` : '/bindings'), intent.command, { headers: intent.etag ? { 'If-Match': intent.etag } : {}, signal })),
  original: async (id: string, signal?: AbortSignal) => data<BindingQuery>(await http.get(root + `/commands/${encodeURIComponent(id)}`, { signal })),
}
