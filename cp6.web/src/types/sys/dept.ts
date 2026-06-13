// PUB 章00 组织模型 —— 部门类型
export interface DeptTreeNode {
  id: string
  parentId?: string | null
  deptCode: string
  deptName: string
  leaderId?: string | null
  leaderName?: string | null
  sort: number
  enable: boolean
  children: DeptTreeNode[]
}

export interface DeptForm {
  id?: string
  parentId?: string | null
  deptCode: string
  deptName: string
  leaderId?: string | null
  sort: number
  enable: boolean
}
