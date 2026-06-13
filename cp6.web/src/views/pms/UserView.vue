<template>
  <div>
    <VolTable :columns="columns" :api="userApi" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { userApi } from '@/api/sys/user'
import { roleApi } from '@/api/sys/role'
import { deptApi } from '@/api/sys/dept'

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
  { prop: 'deptId', label: '所属部门', formType: 'select', options: deptOptions.value, tableHidden: true },
  { prop: 'managerId', label: '直属上级', formType: 'select', options: userOptions.value, tableHidden: true },
  { prop: 'email', label: '邮箱' },
  { prop: 'enable', label: t('user.enable'), width: 80, type: 'switch', formType: 'switch' },
  { prop: 'createDate', label: t('user.createDate'), width: 180, formType: 'none' }
])

function flattenDept(nodes: any[], acc: { label: string; value: string }[] = []) {
  for (const n of nodes) {
    acc.push({ label: n.deptName, value: n.id })
    if (n.children?.length) flattenDept(n.children, acc)
  }
  return acc
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
