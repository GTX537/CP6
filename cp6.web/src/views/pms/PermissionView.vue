<template>
  <div style="padding: 20px">
    <h2>{{ $t('permission.title') }}</h2>

    <div style="display: flex; gap: 20px">
      <!-- 左侧：选择角色 -->
      <el-card style="width: 300px">
        <template #header>{{ $t('permission.selectRole') }}</template>
        <el-radio-group v-model="selectedRoleId" @change="loadRoleMenus">
          <el-radio
            v-for="role in roles"
            :key="role.roleId"
            :value="role.roleId"
            style="display: block; margin-bottom: 12px"
          >
            {{ role.roleName }}
          </el-radio>
        </el-radio-group>
      </el-card>

      <!-- 右侧：菜单树 -->
      <el-card style="flex: 1">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <span>{{ $t('permission.menuPermission') }}</span>
            <el-button type="primary" :disabled="!selectedRoleId" @click="savePermission">
              {{ $t('permission.save') }}
            </el-button>
          </div>
        </template>
        <el-tree
          ref="treeRef"
          :data="menuTree"
          show-checkbox
          node-key="menuId"
          :props="{ label: 'menuName', children: 'children' }"
          default-expand-all
        />
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import type { ElTree } from 'element-plus'
import { roleApi } from '@/api/sys/role'
import { menuApi } from '@/api/sys/menu'

const { t } = useI18n()

const roles = ref<any[]>([])
const selectedRoleId = ref<number | null>(null)
const menuTree = ref<any[]>([])
const treeRef = ref<InstanceType<typeof ElTree>>()

function buildTree(list: any[], parentId: number | null = null): any[] {
  return list
    .filter((item) => item.parentId === parentId)
    .map((item) => ({
      ...item,
      children: buildTree(list, item.menuId)
    }))
}

async function loadData() {
  const [roleRes, menuRes]: any[] = await Promise.all([roleApi.getAll(), menuApi.getAll()])
  roles.value = roleRes
  menuTree.value = buildTree(menuRes)
}

async function loadRoleMenus(roleId: number) {
  const menuIds: any = await roleApi.getRoleMenus(roleId)
  treeRef.value?.setCheckedKeys(menuIds)
}

async function savePermission() {
  const checked = treeRef.value?.getCheckedKeys() as number[]
  const halfChecked = treeRef.value?.getHalfCheckedKeys() as number[]
  const allIds = [...checked, ...halfChecked]

  await roleApi.saveRoleMenus(selectedRoleId.value!, allIds)
  ElMessage.success(t('permission.saveSuccess'))
}

onMounted(loadData)
</script>
