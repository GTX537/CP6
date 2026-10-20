import http from '../http'

export interface IamScope { tenantId: string; actorId: string }
export type IamKind = 'user-create' | 'user-update' | 'user-roles' | 'user-org'
  | 'dept-create' | 'dept-update' | 'dept-move' | 'dept-leader' | 'dept-delete'
export interface IamOperation { operationId: string; kind: IamKind; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; entityId?: string; version?: string; referencesVersion?: string }
export interface IamUser { id: string; userName: string; nickName: string | null; enable: boolean; roleId: number | null; roleIds: number[]; deptId: string | null; managerId: string | null; email: string | null; version: string; mustChangePassword: boolean; locked: boolean }
export interface IamUserEditor extends IamScope {
  referencesVersion: string; user: IamUser | null
  roles: { roleId: number; roleName: string; enable: boolean }[]
  departments: { id: string; deptName: string; parentId: string | null; enable: boolean; available: boolean }[]
  managers: { id: string; userName: string; nickName: string | null; enable: boolean }[]
}
export interface IamUserInput { id?: string; userName: string; nickName: string | null; password?: string; enable: boolean; roleId: number | null; roleIds: number[]; deptId: string | null; managerId: string | null; email: string | null; expectedVersion: string; expectedReferencesVersion: string; reason: string }
export const iamApi = {
  async userEditor(id?: string): Promise<IamUserEditor> { return await http.get('/iam/users/editor', { params: { id } }) as unknown as IamUserEditor },
  async saveUser(input: IamUserInput, operationId: string): Promise<IamOperation> {
    return await (input.id ? http.put(`/iam/users/${input.id}`, { ...input, operationId }) : http.post('/iam/users', { ...input, operationId })) as unknown as IamOperation
  },
  async resolve(operationId: string, kind: IamKind, close = false): Promise<IamOperation> {
    const path = `/iam/users/operations/${operationId}${close ? '/close' : ''}`
    return await (close ? http.post(path, null, { params: { kind } }) : http.get(path, { params: { kind } })) as unknown as IamOperation
  }
}
