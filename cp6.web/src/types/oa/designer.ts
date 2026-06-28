export interface FlowDefSummary {
  flowKey: string
  flowName: string
  formKey: string
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
}

export interface LoadFlowResult {
  summary: FlowDefSummary
  schemaJson: string
}
