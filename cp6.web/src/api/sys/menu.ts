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

export const menuApi = {
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
