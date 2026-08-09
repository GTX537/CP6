export interface PendingItem {
  taskId: string
  instanceId: string
  tokenId?: string
  flowKey: string
  flowName?: string
  nodeId: string
  nodeName?: string
  starterId: string
  starterName: string
  bizType?: string
  bizId?: string
  isRead: boolean
  sentAt: string
  stageIndex?: number
  stageRound?: number
  stageName?: string
  stageCode?: string
  canSendBackPrevStage?: boolean
  detailRoute?: string
}

export interface CcItem {
  ccId: string
  instanceId: string
  flowKey: string
  flowName?: string
  atNodeId?: string
  starterId: string
  starterName: string
  isRead: boolean
  createDate: string
}

export interface RunningItem {
  instanceId: string
  flowKey: string
  flowName?: string
  currentNode: string
  status: number
  currentHandlers: string[]
  createDate: string
}

export interface DoneItem {
  instanceId: string
  flowKey: string
  flowName?: string
  starterId: string
  starterName: string
  formToStatus: number
  doneAt: string
  instanceStatus: number
}

export interface TrendPoint {
  date: string
  count: number
}

export interface InboxStats {
  pendingCount: number
  runningCount: number
  doneThisMonth: number
  rejectedBackToMe: number
  trend: TrendPoint[]
  recentPending: PendingItem[]
}

export interface TimelineRow {
  stepSeq: number
  tokenId?: string
  nodeId: string
  nodeName?: string
  expectedHandlerId: string
  expectedHandlerName: string
  actualHandlerId?: string
  actualHandlerName?: string
  onBehalfOfId?: string
  onBehalfOfName?: string
  status: number
  comment?: string
  sentAt: string
  handledAt?: string
  stageIndex?: number | null
  stageRound?: number | null
}

export interface SnapshotRow {
  stepSeq: number
  nodeId: string
  dataJson: string
}

export interface CcRow {
  recipientId: string
  recipientName: string
  atNodeId?: string
  isRead: boolean
}

export interface ForecastStep {
  nodeId: string
  nodeName?: string
  type: string
  approvers: string[]
  resolved: boolean
  note?: string
}

export interface InboxDetail {
  instance: {
    id: string
    flowKey: string
    flowName?: string
    flowVersion?: number
    status: number
    currentNodeId: string
    currentNodeName?: string
    starter: { id: string; name: string }
    createdAtUtc: string
  }
  content: {
    kind: 'sfs' | 'business'
    formDataId?: string
    formKey?: string
    formVersion?: number
    schemaJson?: string
    dataJson?: string
    fieldMask?: Record<string, 'edit' | 'readonly'>
    bizType?: string
    bizId?: string
  }
  myTask?: {
    taskId: string
    nodeId: string
    fieldMask: Record<string, 'edit' | 'readonly'>
    formDataRowVersion?: string
  } | null
  timeline: TimelineRow[]
  snapshots: SnapshotRow[]
  forecast: ForecastStep[]
  cc: CcRow[]
  subFlowParent?: { instanceId: string; flowKey: string; flowName?: string } | null
  subFlows?: Array<{ instanceId: string; subIndex: number; flowKey: string; flowName?: string; status: number; nodeId: string }>
}

export interface BatchResultItem {
  taskId: string
  ok: boolean
  error?: string
}

export interface FlowAdminItem {
  flowKey: string
  flowName: string
  formKey: string
  version: number
  enable: boolean
}

// ── 在途批量转单（wfs-inbox-ux §3）──
export interface BatchTransferReq {
  fromUserId: string
  toUserId: string
  comment?: string
  filter?: { flowKey?: string; beforeUtc?: string; taskIds?: string[] }
}

export interface BatchTransferItemResult {
  taskId: string
  flowKey: string
  ok: boolean
  error?: string
}

export interface BatchTransferReport {
  total: number
  succeeded: number
  failed: BatchTransferItemResult[]
}

export interface BatchTransferPreview {
  total: number
  sample: PendingItem[]
}
