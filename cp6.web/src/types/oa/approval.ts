export interface ApprovalPanelTimelineItem {
  stepSeq: number
  nodeId: string
  nodeName?: string
  expectedHandlerName: string
  actualHandlerName?: string
  status: number
  comment?: string
  sentAt: string
  handledAt?: string
}

export interface ApprovalPanelDetail {
  bizType: string
  bizId: string
  businessStatus: string
  approvalStatus: 'none' | 'running' | 'approved' | 'rejected' | 'withdrawn' | 'suspended' | 'unknown'
  instanceId?: string
  myTask?: {
    taskId: string
    nodeId: string
    actions: Array<'approve' | 'reject'>
  }
  timeline: ApprovalPanelTimelineItem[]
  canSubmit: boolean
  detailRoute?: string
}
