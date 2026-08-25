<template>
  <div>
    <VolTable :columns="columns" :api="userApi">
      <template #extra-actions="{ row }">
        <el-button link type="warning" @click="openRoleDialog(row)">{{ t('角色') }}</el-button>
      </template>
    </VolTable>

    <!-- PUB 章01：用户角色分配（多角色 + 主角色） -->
    <el-dialog v-model="roleDialogVisible" :title="t('分配角色 — {name}', { name: roleDialogUserName })" width="600px">
      <el-transfer
        v-model="assignedRoleIds"
        :data="transferData"
        :titles="[t('可选角色'), t('已分配')]"
        filterable
      />
      <div style="margin-top: 16px;">
        <span style="margin-right: 8px;">{{ t('主角色（默认/显示用）：') }}</span>
        <el-select v-model="primaryRoleId" :placeholder="t('选择主角色')" clearable style="width: 240px;">
          <el-option v-for="r in primaryRoleOptions" :key="r.value" :label="r.label" :value="r.value" />
        </el-select>
      </div>
      <template #footer>
        <el-button @click="roleDialogVisible = false">{{ t('取消') }}</el-button>
        <el-button type="primary" @click="saveRoles">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { userApi } from '@/api/sys/user'
import { roleApi } from '@/api/sys/role'
import { deptApi } from '@/api/sys/dept'
import { userRoleApi } from '@/api/sys/userRole'

const { t } = useI18n()
const roleOptions = ref<{ label: string; value: number }[]>([])
const deptOptions = ref<{ label: string; value: string }[]>([])
const userOptions = ref<{ label: string; value: string }[]>([])

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'userName', label: t('user.userName'), required: true },
  { prop: 'password', label: t('user.password'), formType: 'password', tableHidden: true },
  { prop: 'nickName', label: t('user.nickName') },
  { prop: 'roleId', label: t('user.role'), formType: 'select', options: roleOptions.value },
  // PUB 章00 组织字段（表单可填，列表默认隐藏部门/上级，邮箱展示）
  { prop: 'deptId', label: t('所属部门'), formType: 'select', options: deptOptions.value, tableHidden: true },
  { prop: 'managerId', label: t('直属上级'), formType: 'select', options: userOptions.value, tableHidden: true },
  { prop: 'email', label: t('邮箱') },
  { prop: 'enable', label: t('user.enable'), width: 80, type: 'switch', formType: 'switch' },
  { prop: 'createDate', label: t('user.createDate'), width: 180, type: 'datetime', formType: 'none' }
])

function flattenDept(nodes: any[], acc: { label: string; value: string }[] = []) {
  for (const n of nodes) {
    acc.push({ label: n.deptName, value: n.id })
    if (n.children?.length) flattenDept(n.children, acc)
  }
  return acc
}

// ───── PUB 章01 角色分配对话框 ─────
const roleDialogVisible = ref(false)
const roleDialogUserId = ref<string>('')
const roleDialogUserName = ref<string>('')
const assignedRoleIds = ref<number[]>([])
const primaryRoleId = ref<number | null>(null)

const transferData = computed(() => roleOptions.value.map(r => ({ key: r.value, label: r.label })))
// 主角色只能从已分配角色里选
const primaryRoleOptions = computed(() => roleOptions.value.filter(r => assignedRoleIds.value.includes(r.value)))

// 取消勾选某角色后，若它正是主角色则清空主角色
watch(assignedRoleIds, (ids) => {
  if (primaryRoleId.value != null && !ids.includes(primaryRoleId.value)) primaryRoleId.value = null
})

async function openRoleDialog(row: any) {
  roleDialogUserId.value = row.id
  roleDialogUserName.value = row.nickName || row.userName
  const cur = await userRoleApi.get(row.id)
  assignedRoleIds.value = cur.roleIds ?? []
  primaryRoleId.value = cur.primaryRoleId ?? null
  roleDialogVisible.value = true
}

async function saveRoles() {
  await userRoleApi.save(roleDialogUserId.value, assignedRoleIds.value, primaryRoleId.value)
  ElMessage.success(t('角色已保存'))
  roleDialogVisible.value = false
}

onMounted(async () => {
  const res = await roleApi.getAll() as any
  roleOptions.value = res.map((r: any) => ({ label: r.roleName, value: r.roleId }))

  deptOptions.value = flattenDept(await deptApi.tree())

  const ur: any = await userApi.getList({ page: 1, pageSize: 1000 })
  const rows: any[] = ur.rows ?? ur.data ?? ur ?? []
  userOptions.value = rows.map((u: any) => ({ label: u.nickName || u.userName, value: u.id }))
})
</script>
