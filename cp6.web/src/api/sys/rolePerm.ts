import http from '../http'
import type {
  MenuActionDto, MenuActionFullDto, RolePermDto,
  DataScopeResourceDto, RoleDataScopeDto,
  FieldResourceDto, RoleFieldPermDto
} from '@/types/sys/rolePerm'

// 后端 RolePermController 返回 {code,message,data} 信封；http 拦截器已返回 body。
export const rolePermApi = {
  async getMenuActions(menuId: number): Promise<MenuActionDto[]> {
    const res: any = await http.get(`/pub/role-perm/menu-action/${menuId}`)
    return res.data ?? []
  },
  async getAllMenuActions(): Promise<MenuActionFullDto[]> {
    const res: any = await http.get('/pub/role-perm/menu-action/all')
    return res.data ?? []
  },
  saveMenuActions(menuId: number, actions: MenuActionDto[]) {
    return http.put(`/pub/role-perm/menu-action/${menuId}`, actions)
  },
  async getRolePerm(roleId: number): Promise<RolePermDto> {
    const res: any = await http.get(`/pub/role-perm/${roleId}`)
    return res.data ?? { menuIds: [], actions: [] }
  },
  saveRolePerm(roleId: number, dto: RolePermDto) {
    return http.put(`/pub/role-perm/${roleId}`, dto)
  },
  async myActions(): Promise<string[]> {
    const res: any = await http.get('/pub/role-perm/my-actions')
    return res.data ?? []
  },

  // 章03 数据权限
  async getDataScopeResources(): Promise<DataScopeResourceDto[]> {
    const res: any = await http.get('/pub/role-perm/data-scope/resources')
    return res.data ?? []
  },
  async getRoleDataScopes(roleId: number): Promise<RoleDataScopeDto[]> {
    const res: any = await http.get(`/pub/role-perm/data-scope/${roleId}`)
    return res.data ?? []
  },
  saveRoleDataScopes(roleId: number, items: RoleDataScopeDto[]) {
    return http.put(`/pub/role-perm/data-scope/${roleId}`, items)
  },

  // 章04 字段权限
  async getFieldResources(): Promise<FieldResourceDto[]> {
    const res: any = await http.get('/pub/role-perm/field-perm/resources')
    return res.data ?? []
  },
  async getRoleFieldPerms(roleId: number): Promise<RoleFieldPermDto[]> {
    const res: any = await http.get(`/pub/role-perm/field-perm/${roleId}`)
    return res.data ?? []
  },
  saveRoleFieldPerms(roleId: number, items: RoleFieldPermDto[]) {
    return http.put(`/pub/role-perm/field-perm/${roleId}`, items)
  },
  async myReadonly(resourceKey: string): Promise<string[]> {
    const res: any = await http.get(`/pub/role-perm/my-readonly/${resourceKey}`)
    return res.data ?? []
  },
}
