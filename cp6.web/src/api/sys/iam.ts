import http from '../http'

export interface IamScope { tenantId: string; actorId: string }
export type IamKind = 'user-create' | 'user-update' | 'user-roles' | 'user-org'
  | 'dept-create' | 'dept-update' | 'dept-move' | 'dept-leader' | 'dept-delete' | 'dept-order-mapping' | 'dept-standalone-mapping'
  | 'role-create' | 'role-update' | 'role-delete'
  | 'role-policy-actions' | 'role-policy-scopes' | 'role-policy-fields' | 'menu-actions'
export interface IamOperation { operationId: string; kind: IamKind; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; entityId?: string; roleId?: number; version?: string; referencesVersion?: string; treeVersion?: string }
export interface IamUser { id: string; userName: string; nickName: string | null; enable: boolean; roleId: number | null; roleIds: number[]; deptId: string | null; managerId: string | null; email: string | null; version: string; mustChangePassword: boolean; locked: boolean }
export interface IamUserEditor extends IamScope {
  referencesVersion: string; user: IamUser | null
  roles: { roleId: number; roleName: string; enable: boolean }[]
  departments: { id: string; deptName: string; parentId: string | null; enable: boolean; available: boolean }[]
  managers: { id: string; userName: string; nickName: string | null; enable: boolean }[]
}
export interface IamUserInput { id?: string; userName: string; nickName: string | null; password?: string; enable: boolean; roleId: number | null; roleIds: number[]; deptId: string | null; managerId: string | null; email: string | null; expectedVersion: string; expectedReferencesVersion: string; reason: string }
export interface IamDepartment { id: string; parentId: string | null; deptCode: string; deptName: string; path: string; leaderId: string | null; sort: number; enable: boolean; version: string; available: boolean; diagnostic: string | null }
export interface IamDepartmentEditor extends IamScope { treeVersion: string; referencesVersion: string; departments: IamDepartment[]; leaders: IamUserEditor['managers'] }
export interface IamDepartmentInput { id?: string; parentId: string | null; deptCode: string; deptName: string; leaderId: string | null; sort: number; enable: boolean; expectedVersion: string; expectedTreeVersion: string; expectedReferencesVersion: string; reason: string; previewVersion?: string }
export interface IamDepartmentPreview { id: string; oldParentId: string | null; newParentId: string | null; treeVersion: string; referencesVersion: string; version: string; affectedDescendantCount: number; affectedUsersCount: number; scopeRefs: { roleId: number; resourceKey: string; scopeType: number }[]; approvalRouteWarnings: string[]; previewVersion: string }
export interface IamDepartmentReferences { id: string; childCount: number; userCount: number; scopeRefs: IamDepartmentPreview['scopeRefs']; deletionAllowed: boolean; unverifiedReferenceCategories: string[] }
export type StandaloneKind = 'stock' | 'stock-transaction' | 'stocktake' | 'work-order' | 'machine' | 'replenish-order' | 'mobile-task' | 'work-center' | 'cross-dock'
export interface IamStandaloneMapping { id: string; objectKind: StandaloneKind; objectId: string; objectName?: string; departmentId: string; enabled: boolean; available: boolean; version: string }
export interface IamStandaloneMappingEditor extends IamScope { referencesVersion: string; mappings: IamStandaloneMapping[]; departments: { id: string; name: string; available: boolean }[] }
export interface IamStandaloneMappingInput { id?: string; objectKind: StandaloneKind; objectId: string; departmentId: string; enabled: boolean; expectedVersion: string; expectedReferencesVersion: string; reason: string }
export interface StandaloneObjectChoice { id: string; name: string }
export interface IamOrderMapping { id: string; code: string; departmentId: string; enabled: boolean; available: boolean; version: string; orderCount: number }
export interface IamOrderMappingEditor extends IamScope { referencesVersion: string; mappings: IamOrderMapping[]; departments: { id: string; name: string; available: boolean }[] }
export interface IamOrderMappingInput { id?: string; code: string; departmentId: string; enabled: boolean; expectedVersion: string; expectedReferencesVersion: string; reason: string }
export interface IamRole { roleId: number; roleName: string; description: string | null; enable: boolean; orderNo: number; createDate: string; version: string }
export interface IamRoleEditor extends IamScope { referencesVersion: string; roles: IamRole[] }
export interface IamRoleInput { roleId: number; roleName: string; description: string | null; enable: boolean; orderNo: number; expectedVersion: string; expectedReferencesVersion: string; reason: string }
export interface IamRoleReferences { roleId: number; primaryUsers: number; additionalUsers: number; menus: number; actions: number; dataScopes: number; fields: number; ssoDefaults: number; ssoApproved: boolean; deletionAllowed: boolean; unverifiedReferenceCategories: string[] }
export const iamApi = {
  async standaloneMappings(): Promise<IamStandaloneMappingEditor> { return await http.get('/iam/departments/standalone-mappings') as unknown as IamStandaloneMappingEditor },
  async standaloneObjects(kind: StandaloneKind, search: string): Promise<StandaloneObjectChoice[]> {
    return await http.get('/iam/departments/standalone-objects', { params: { kind, search } }) as unknown as StandaloneObjectChoice[]
  },
  async saveStandaloneMapping(input: IamStandaloneMappingInput, operationId: string): Promise<IamOperation> {
    return await http.post('/iam/departments/standalone-mappings', { ...input, operationId }) as unknown as IamOperation
  },
  async orderMappings(): Promise<IamOrderMappingEditor> { return await http.get('/iam/departments/order-mappings') as unknown as IamOrderMappingEditor },
  async saveOrderMapping(input: IamOrderMappingInput, operationId: string): Promise<IamOperation> {
    return await http.post('/iam/departments/order-mappings', { ...input, operationId }) as unknown as IamOperation
  },
  async roleEditor(): Promise<IamRoleEditor> { return await http.get('/iam/roles/editor') as unknown as IamRoleEditor },
  async roleReferences(id: number): Promise<IamRoleReferences> { return await http.get(`/iam/roles/${id}/references`) as unknown as IamRoleReferences },
  async saveRole(input: IamRoleInput, create: boolean, operationId: string): Promise<IamOperation> {
    const body = { ...input, operationId }
    return await (create ? http.post('/iam/roles', body) : http.put(`/iam/roles/${input.roleId}`, body)) as unknown as IamOperation
  },
  async userEditor(id?: string): Promise<IamUserEditor> { return await http.get('/iam/users/editor', { params: { id } }) as unknown as IamUserEditor },
  async saveUser(input: IamUserInput, operationId: string): Promise<IamOperation> {
    return await (input.id ? http.put(`/iam/users/${input.id}`, { ...input, operationId }) : http.post('/iam/users', { ...input, operationId })) as unknown as IamOperation
  },
  async departmentEditor(): Promise<IamDepartmentEditor> { return await http.get('/iam/departments/editor') as unknown as IamDepartmentEditor },
  async departmentPreview(id: string, parentId: string | null): Promise<IamDepartmentPreview> { return await http.get(`/iam/departments/${id}/preview`, { params: { parentId } }) as unknown as IamDepartmentPreview },
  async departmentReferences(id: string): Promise<IamDepartmentReferences> { return await http.get(`/iam/departments/${id}/references`) as unknown as IamDepartmentReferences },
  async saveDepartment(input: IamDepartmentInput, kind: IamKind, operationId: string): Promise<IamOperation> {
    const body = { ...input, operationId }
    if (kind === 'dept-create') return await http.post('/iam/departments', body) as unknown as IamOperation
    if (kind === 'dept-move') return await http.post(`/iam/departments/${input.id}/move`, body) as unknown as IamOperation
    if (kind === 'dept-update') return await http.put(`/iam/departments/${input.id}`, body) as unknown as IamOperation
    throw new Error('未支持的组织操作。')
  },
  async resolve(operationId: string, kind: IamKind, close = false): Promise<IamOperation> {
    const group = kind.startsWith('role-policy-') || kind === 'menu-actions' ? 'role-policies' : kind.startsWith('role-') ? 'roles' : kind.startsWith('dept-') ? 'departments' : 'users'
    const path = `/iam/${group}/operations/${operationId}${close ? '/close' : ''}`
    return await (close ? http.post(path, null, { params: { kind } }) : http.get(path, { params: { kind } })) as unknown as IamOperation
  }
}
