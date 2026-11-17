import http from '../http'
import type {
  BasisSubmission, FinanceOptions, RateDraft, RateMaintenance, RatePreview, RateVersion, StandardBasis,
  StandardCommand, StandardCommandQuery, StandardCommandResult, StandardDocumentView, WorkOrderCostInputs,
} from '@/types/fin/manufacturingCost'

const root = '/fin/cost/owner-v1'
const context = (token: string) => ({ headers: { 'X-Finance-Context': token } })
const segment = encodeURIComponent

export const manufacturingCostApi = {
  options(legalEntityKey?: string, ledgerKey?: string) {
    return http.get<unknown, FinanceOptions>('/fin/cost/v1/options', { params: { legalEntityKey, ledgerKey } })
  },
  rates(token: string, search: string, businessDate: string) {
    return http.get<unknown, RateMaintenance>(root + '/rate-maintenance', {
      ...context(token), params: { search, businessDate },
    })
  },
  draftRate(token: string, payload: RateDraft) {
    return http.post<unknown, Pick<RateVersion, 'rateId' | 'state' | 'revision'>>(root + '/rates', payload, context(token))
  },
  transitionRate(token: string, id: string, action: 'submit' | 'review', expectedRevision: number) {
    return http.post<unknown, Pick<RateVersion, 'rateId' | 'state' | 'revision'>>(
      root + '/rates/' + segment(id) + '/' + action, { expectedRevision }, context(token))
  },
  previewRate(token: string, id: string, expectedRevision: number) {
    return http.post<unknown, RatePreview>(root + '/rates/' + segment(id) + '/release-preview', { expectedRevision }, context(token))
  },
  publishRate(token: string, id: string, previewToken: string) {
    return http.post<unknown, Pick<RateVersion, 'rateId' | 'state' | 'revision'>>(
      root + '/rates/' + segment(id) + '/publish', { previewToken }, context(token))
  },
  workOrder(token: string, workOrderNo: string) {
    return http.get<unknown, WorkOrderCostInputs>(root + '/work-order-maintenance', {
      ...context(token), params: { workOrderNo },
    })
  },
  basis(token: string, id: string) {
    return http.get<unknown, StandardBasis>(root + '/standard-bases/' + segment(id), context(token))
  },
  submitBasis(token: string, payload: BasisSubmission) {
    return http.post<unknown, { versionId: string }>(root + '/standard-bases', payload, context(token))
  },
  transitionBasis(token: string, id: string, action: 'review' | 'publish' | 'revoke',
    payload: { commandId: string; expectedRevision: number; reason?: string }) {
    return http.post<unknown, { versionId: string }>(
      root + '/standard-bases/' + segment(id) + '/' + action, payload, context(token))
  },
  document(token: string, id: string, version?: string) {
    return http.get<unknown, StandardDocumentView>(
      root + '/standard-documents/' + segment(id) + (version ? '/versions/' + segment(version) : ''), context(token))
  },
  writeDocument(token: string, command: StandardCommand) {
    const documents = root + '/standard-documents'
    const path = command.operation === 'CREATE' ? documents : documents + '/' + segment(command.documentId)
      + (command.operation === 'PREPARE' ? '/collection-plans' : '/freeze')
    return http.post<unknown, StandardCommandResult>(path, command.input, context(token))
  },
  queryCommand(token: string, commandId: string) {
    return http.get<unknown, StandardCommandQuery>(root + '/standard-document-commands/' + segment(commandId), context(token))
  },
}

const messages: Record<string, string> = {
  FINANCE_CONTEXT_NOT_FOUND: '账套访问已变化，请重新选择账套。',
  FINANCE_QUALIFICATION_REQUIRED: '当前操作需要本账套有效的专业资格。',
  STANDARD_BASIS_WORK_ORDER_NOT_FOUND: '当前账套和权限范围内找不到此工单。',
  STANDARD_BASIS_TARGET_MISMATCH: '工单的完工数量须与批准标准的目标数量一致。',
  STANDARD_BASIS_REVOKED: '此标准依据已撤销。',
  STANDARD_BASIS_NOT_EFFECTIVE: '此标准依据在当前会计日无效。',
  PUBLISHED_RATE_UNAVAILABLE: '本工单有工序缺少可用的已发布费率。',
  COST_CENTER_VERSION_UNAVAILABLE: '成本中心版本与所选工序或整个有效区间不匹配。',
  APPROVED_RATE_BASIS_REQUIRED: '请核对费率依据、有效区间和零费率批准。',
  COMMAND_IDENTITY_CONFLICT: '原命令与当前提交内容不一致，请先回查原命令。',
}

export function maintenanceMessage(error: unknown, fallback = '操作未完成，请核对当前账套、权限与版本。') {
  const response = (error as { response?: { data?: { message?: string }; status?: number } } | null)?.response
  const code = typeof error === 'string' ? error : response?.data?.message
  if (response?.status === 412) return '所用版本或计划已失效，请读取当前版本后重新准备。'
  if (response?.status === 403 && (!code || !messages[code])) return '当前账号没有此操作权限或来源读取权限。'
  return (code && messages[code]) || fallback
}
