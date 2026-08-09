export interface FlowDefSummary {
  flowKey: string
  flowName: string
  formKey?: string
  functionId?: string
  flowCode?: string
  version: number
  enable: boolean
}

export interface SaveFlowBody {
  flowKey: string
  flowName: string
  formKey: string
  functionId?: string
  flowCode?: string
  schemaJson: string
  rowVersion?: string
}

export interface LoadFlowResult {
  summary: FlowDefSummary
  schemaJson: string
  draft?: { rowVersion?: string; version: number }
}
