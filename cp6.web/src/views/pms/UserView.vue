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
import { userApi } from '@/api/user'
import { roleApi } from '@/api/role'

const { t } = useI18n()
const roleOptions = ref<{ label: string; value: number }[]>([])

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'userName', label: t('user.userName'), required: true },
  { prop: 'password', label: t('user.password'), formType: 'password', tableHidden: true },
  { prop: 'nickName', label: t('user.nickName') },
  { prop: 'roleId', label: t('user.role'), formType: 'select', options: roleOptions.value },
  { prop: 'enable', label: t('user.enable'), width: 80, type: 'switch', formType: 'switch' },
  { prop: 'createDate', label: t('user.createDate'), width: 180, formType: 'none' }
])

onMounted(async () => {
  const res = await roleApi.getAll() as any
  roleOptions.value = res.map((r: any) => ({ label: r.roleName, value: r.roleId }))
})
</script>
