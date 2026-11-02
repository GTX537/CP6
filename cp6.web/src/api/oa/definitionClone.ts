import http from '../http'
export type DefinitionKind = 'FLOW' | 'FORM'
export interface CloneSource { key: string; versionId: string; version: number }
export interface CloneIntent {
  kind: DefinitionKind; sourceKey: string; unknown?: boolean
  command: { commandId: string; expectedVersion: null; body: { sourceVersionId: string; newKey: string; newName: string } }
}
export interface CloneResult {
  commandId: string; outcome: string
  result: { key: string; clonedFromRef?: string | null } | null
}
const data = <T>(value: any): T => value?.data ?? value
export const definitionCloneApi = {
  execute: async (intent: CloneIntent, signal?: AbortSignal) => data<CloneResult>(await http.post(
    `/oa/v2/definitions/${intent.kind}/${encodeURIComponent(intent.sourceKey)}/clone`, intent.command, { signal })),
  original: async (intent: CloneIntent, signal?: AbortSignal) => data<CloneResult>(await http.get(
    `/oa/v2/commands/${encodeURIComponent(intent.command.commandId)}`, { signal, params: { operation: `${intent.kind}_CLONE`, resource: intent.command.body.newKey } })),
}
