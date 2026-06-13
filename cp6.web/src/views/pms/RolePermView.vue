<template>
  <div style="padding: 20px">
    <h2>角色功能权限（菜单 + 操作点）</h2>

    <div style="display: flex; gap: 20px">
      <!-- 左：角色 -->
      <el-card style="width: 280px">
        <template #header>选择角色</template>
        <el-radio-group v-model="selectedRoleId" @change="loadRolePerm">
          <el-radio
            v-for="role in roles"
            :key="role.roleId"
            :value="role.roleId"
            style="display: block; margin-bottom: 12px"
          >{{ role.roleName }}</el-radio>
        </el-radio-group>
      </el-card>

      <!-- 右：菜单树 + 操作点 -->
      <el-card style="flex: 1">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <span>菜单与操作点（未勾选菜单则其操作点禁用）</span>
            <el-button type="primary" :disabled="!selectedRoleId" @click="save">保存</el-button>
          </div>
        </template>
        <el-tree
          ref="treeRef"
          :data="menuTree"
          show-checkbox
          node-key="menuId"
          :props="{ label: 'menuName', children: 'children' }"
          default-expand-all
          @check="onCheck"
        >
          <template #default="{ data }">
            <span>{{ data.menuName }}</span>
            <span v-if="actionsOf(data.menuId).length" style="margin-left: 12px">
              <el-checkbox
                v-for="act in actionsOf(data.menuId)"
                :key="act.actionCode"
                :model-value="checkedActions.has(`${data.menuId}:${act.actionCode}`)"
                :disabled="!checkedMenuIds.has(data.menuId)"
                style="margin-right: 10px"
                @change="(v: any) => toggleAction(data.menuId, act.actionCode, !!v)"
                @click.stop
              >{{ act.actionName || act.actionCode }}</el-checkbox>
            </span>
          </template>
        </el-tree>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import type { ElTree } from 'element-plus'
import { roleApi } from '@/api/sys/role'
import { menuApi } from '@/api/sys/menu'
import { rolePermApi } from '@/api/sys/rolePerm'
import type { MenuActionFullDto } from '@/types/sys/rolePerm'

const roles = ref<any[]>([])
const selectedRoleId = ref<number | null>(null)
const menuTree = ref<any[]>([])
const treeRef = ref<InstanceType<typeof ElTree>>()
const menuActions = ref<Record<number, MenuActionFullDto[]>>({})
const checkedActions = ref<Set<string>>(new Set())     // "menuId:actionCode"
const checkedMenuIds = ref<Set<number>>(new Set())

function buildTree(list: any[], parentId: number | null = null): any[] {
  return list
    .filter((item) => item.parentId === parentId)
    .map((item) => ({ ...item, children: buildTree(list, item.menuId) }))
}

function actionsOf(menuId: number): MenuActionFullDto[] {
  return menuActions.value[menuId] ?? []
}

async function loadData() {
  const [roleRes, menuRes, actRes]: any[] = await Promise.all([
    roleApi.getAll(), menuApi.getAll(), rolePermApi.getAllMenuActions()
  ])
  roles.value = roleRes
  menuTree.value = buildTree(menuRes)
  const map: Record<number, MenuActionFullDto[]> = {}
  for (const a of actRes as MenuActionFullDto[]) (map[a.menuId] ??= []).push(a)
  menuActions.value = map
}

async function loadRolePerm(roleId: number) {
  const perm = await rolePermApi.getRolePerm(roleId)
  treeRef.value?.setCheckedKeys(perm.menuIds ?? [])
  syncCheckedMenuIds()
  checkedActions.value = new Set((perm.actions ?? []).map(a => `${a.menuId}:${a.actionCode}`))
}

function syncCheckedMenuIds() {
  const checked = (treeRef.value?.getCheckedKeys() as number[]) ?? []
  const half = (treeRef.value?.getHalfCheckedKeys() as number[]) ?? []
  checkedMenuIds.value = new Set([...checked, ...half])
}

function onCheck() {
  syncCheckedMenuIds()
  // 取消授权的菜单 → 同步清掉其操作点勾选
  for (const key of [...checkedActions.value]) {
    const mid = Number(key.split(':')[0])
    if (!checkedMenuIds.value.has(mid)) checkedActions.value.delete(key)
  }
}

function toggleAction(menuId: number, code: string, checked: boolean) {
  const key = `${menuId}:${code}`
  if (checked) checkedActions.value.add(key)
  else checkedActions.value.delete(key)
}

async function save() {
  const checked = (treeRef.value?.getCheckedKeys() as number[]) ?? []
  const half = (treeRef.value?.getHalfCheckedKeys() as number[]) ?? []
  const menuIds = [...checked, ...half]
  const menuSet = new Set(menuIds)
  const actions = [...checkedActions.value]
    .map(k => { const [mid, code] = k.split(':'); return { menuId: Number(mid), actionCode: code } })
    .filter(a => menuSet.has(a.menuId))   // 只保留已授菜单的操作（满足后端 E-PUB-021）
  await rolePermApi.saveRolePerm(selectedRoleId.value!, { menuIds, actions })
  ElMessage.success('已保存')
}

onMounted(loadData)
</script>
