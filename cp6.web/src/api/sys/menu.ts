import http from '../http'

export interface MenuItem {
  menuId: number
  menuName: string
  routePath: string | null
  menuKey: string | null
  icon: string | null
  parentId: number | null
  orderNo: number
  enable: boolean
  createDate?: string
}

export interface MenuTreePosition {
  menuId: number
  parentId: number | null
  orderNo: number
}

export interface MenuTreeCommand {
  operationRef: string
  expectedRevision: string
  positions: MenuTreePosition[]
  reason: string
}
export interface MenuTreeSnapshot { revision: string; menus: MenuItem[] }
export interface MenuDisplayCommand {
  operationRef: string; expectedRevision: string; menuId: number; menuName: string; icon: string | null; enable: boolean; reason: string
}
export interface MenuTreeImpact {
  revision: string; changedMenuIds: number[]; affectedTenantsCount: number; affectedRolesCount: number
  menuReferences: string; actionReferences: string
}
export interface MenuTreeReceipt { operationRef: string; appliedRevision: string; impact: MenuTreeImpact; committedAt: string }

export const menuApi = {
  displayPreview(command: MenuDisplayCommand): Promise<MenuTreeImpact> { return http.post('/menu/tree-v1/display/preview', command) },
  displayExecute(command: MenuDisplayCommand): Promise<MenuTreeReceipt> { return http.post('/menu/tree-v1/display/commands', command) },
  treeCurrent(): Promise<MenuTreeSnapshot> { return http.get('/menu/tree-v1') },
  treePreview(command: MenuTreeCommand): Promise<MenuTreeImpact> { return http.post('/menu/tree-v1/preview', command) },
  treeExecute(command: MenuTreeCommand): Promise<MenuTreeReceipt> { return http.post('/menu/tree-v1/commands', command) },
  treeOriginal(operationRef: string): Promise<MenuTreeReceipt> { return http.post('/menu/tree-v1/commands/read', { operationRef }) },
  getAll(): Promise<MenuItem[]> {
    return http.get('/menu')
  },
  add(data: MenuItem): Promise<MenuItem> {
    return http.post('/menu', data)
  },
  update(data: MenuItem): Promise<MenuItem> {
    return http.put('/menu', data)
  },
  updateTree(data: MenuTreePosition[]) {
    return http.put('/menu/tree', data)
  },
  del(ids: number[]) {
    return http.delete('/menu', { data: ids })
  }
}
