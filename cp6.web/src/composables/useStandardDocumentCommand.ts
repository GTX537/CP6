import { ref } from 'vue'
import { manufacturingCostApi, maintenanceMessage } from '@/api/fin/manufacturingCost'
import { identityStamp } from '@/auth/identity'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import type { FinanceContext, StandardCommand, StandardReceipt } from '@/types/fin/manufacturingCost'

const storageKey = 'cp6-fin-standard-document-pending'
type Saved = Record<string, StandardCommand>

function readSaved(): Saved {
  try {
    return JSON.parse(sessionStorage.getItem(storageKey) || '{}') ?? {}
  } catch {
    return {}
  }
}

export function useStandardDocumentCommand(context: FinanceContext, canWrite: () => boolean, canRead: () => boolean,
  onCommitted: (reply: StandardReceipt, command: StandardCommand) => Promise<void>) {
  const key = JSON.stringify([identityStamp(), context.scopeKey])
  const saved = readSaved()[key]
  const pending = ref<StandardCommand | null>(saved?.input?.commandId ? saved : null)
  const observedMissing = ref(false)
  const message = ref(pending.value ? '原命令结果待确认，请回查。' : '')
  const receipt = ref<StandardReceipt | null>(null)
  const intent = useOperationsIntent(() => {
    pending.value = null
    observedMissing.value = false
    receipt.value = null
    message.value = ''
  })

  function remember(command: StandardCommand | null) {
    pending.value = command
    try {
      const next = readSaved()
      if (command) next[key] = command
      else delete next[key]
      sessionStorage.setItem(storageKey, JSON.stringify(next))
    } catch {
      // The current tab still holds the original command when session storage is unavailable.
    }
  }

  async function accept(reply: StandardReceipt, command: StandardCommand) {
    if (reply.kind !== 'COMMITTED' || reply.commandId !== command.input.commandId || reply.operation !== command.operation
      || (command.operation !== 'CREATE' && reply.documentId !== command.documentId)) {
      message.value = '未取得匹配的回执，请继续回查原命令。'
      return
    }
    receipt.value = reply
    remember(null)
    observedMissing.value = false
    message.value = '已取得原命令回执。'
    await onCommitted(reply, command)
  }

  async function send(command: StandardCommand, retry: boolean) {
    if (!canWrite() || (retry ? !observedMissing.value : pending.value !== null)) return
    const run = intent.begin()
    if (!run) return
    remember(command)
    observedMissing.value = false
    message.value = ''
    try {
      const reply = await manufacturingCostApi.writeDocument(context.token, command)
      if (!intent.current(run)) return
      if (reply.kind === 'COMMITTED') await accept(reply, command)
      else message.value = '提交结果待确认，请回查原命令。'
    } catch (error) {
      if (!intent.current(run)) return
      const status = (error as { response?: { status?: number } })?.response?.status
      if (!retry && status && status < 500 && status !== 408 && status !== 429) {
        remember(null)
        message.value = maintenanceMessage(error)
      } else {
        message.value = '原命令结果待确认，请回查。'
      }
    } finally {
      intent.finish(run)
    }
  }

  async function query() {
    const command = pending.value
    if (!command || !canRead()) return
    const run = intent.begin()
    if (!run) return
    observedMissing.value = false
    try {
      const result = await manufacturingCostApi.queryCommand(context.token, command.input.commandId)
      if (!intent.current(run)) return
      if (result.kind === 'FOUND') await accept(result.reply, command)
      else if (result.kind === 'NOT_OBSERVED' && result.commandId === command.input.commandId) {
        observedMissing.value = true
        message.value = '当前未查到提交结果。可继续回查，或按原命令重试。'
      }
    } catch (error) {
      if (intent.current(run)) message.value = maintenanceMessage(error, '暂时无法回查，请保留原命令。')
    } finally {
      intent.finish(run)
    }
  }

  return {
    busy: intent.busy, pending, observedMissing, receipt, message, query,
    begin: (command: StandardCommand) => send(command, false),
    retry: () => pending.value ? send(pending.value, true) : undefined,
  }
}
