<template>
  <div style="padding: 20px">
    <h2>角色数据权限</h2>

    <div style="display: flex; gap: 20px">
      <!-- 左：角色 -->
      <el-card style="width: 280px">
        <template #header>选择角色</template>
        <el-radio-group v-model="selectedRoleId" @change="loadRoleScopes">
          <el-radio
            v-for="role in roles"
            :key="role.roleId"
            :value="role.roleId"
            style="display: block; margin-bottom: 12px"
          >{{ role.roleName }}</el-radio>
        </el-radio-group>
      </el-card>

      <!-- 右：资源 + 范围 -->
      <el-card style="flex: 1">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <span>资源数据范围</span>
            <el-button type="primary" :disabled="!selectedRoleId" @click="save">保存</el-button>
          </div>
        </template>

        <el-table :data="resources" border>
          <el-table-column prop="name" label="资源" width="160" />
          <el-table-column label="数据范围" width="200">
            <template #default="{ row }">
              <el-select v-model="scopeMap[row.key]" :disabled="!selectedRoleId" style="width: 100%">
                <el-option
                  v-for="st in row.supports"
                  :key="st"
                  :label="SCOPE_LABELS[st]"
                  :value="st"
                />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="自定义部门（范围=自定义时）">
            <template #default="{ row }">
              <el-tree-select
                v-if="scopeMap[row.key] === 4"
                v-model="customMap[row.key]"
                :data="deptTree"
                :props="{ label: 'deptName', children: 'children' }"
                node-key="id"
                multiple
                show-checkbox
                check-strictly
                style="width: 100%"
                placeholder="选择部门"
              />
              <span v-else style="color: #c0c4cc">—</span>
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { roleApi } from '@/api/sys/role'
import { deptApi } from '@/api/sys/dept'
import { rolePermApi } from '@/api/sys/rolePerm'
import type { DataScopeResourceDto } from '@/types/sys/rolePerm'

const SCOPE_LABELS: Record<number, string> = {
  1: '本人', 2: '本部门', 3: '本部门及下级', 4: '自定义部门', 5: '全部'
}

const roles = ref<any[]>([])
const selectedRoleId = ref<number | null>(null)
const resources = ref<DataScopeResourceDto[]>([])
const deptTree = ref<any[]>([])
const scopeMap = reactive<Record<string, number>>({})
const customMap = reactive<Record<string, string[]>>({})

async function loadData() {
  const [roleRes, resRes, tree]: any[] = await Promise.all([
    roleApi.getAll(), rolePermApi.getDataScopeResources(), deptApi.tree()
  ])
  roles.value = roleRes
  resources.value = resRes
  deptTree.value = tree
}

async function loadRoleScopes(roleId: number) {
  const saved = await rolePermApi.getRoleDataScopes(roleId)
  const savedMap = new Map(saved.map(s => [s.resourceKey, s]))
  for (const r of resources.value) {
    const s = savedMap.get(r.key)
    scopeMap[r.key] = s?.scopeType ?? r.default
    customMap[r.key] = s?.customDeptIds ?? []
  }
}

async function save() {
  const items = resources.value.map(r => ({
    resourceKey: r.key,
    scopeType: scopeMap[r.key] ?? r.default,
    customDeptIds: scopeMap[r.key] === 4 ? (customMap[r.key] ?? []) : []
  }))
  await rolePermApi.saveRoleDataScopes(selectedRoleId.value!, items)
  ElMessage.success('已保存')
}

onMounted(loadData)
</script>
