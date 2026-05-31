<template>
  <div>
    <VolTable :columns="columns" :api="roleApi" id-field="roleId" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { roleApi } from '@/api/role'

const { t } = useI18n()

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'roleId', label: t('role.roleId'), width: 100, required: true },
  { prop: 'roleName', label: t('role.roleName'), required: true },
  { prop: 'description', label: t('role.description'), formType: 'textarea' },
  { prop: 'orderNo', label: t('role.orderNo'), width: 80 },
  { prop: 'enable', label: t('role.enable'), width: 100, type: 'switch', formType: 'switch' },
  { prop: 'createDate', label: t('role.createDate'), width: 180, formType: 'none' }
])
</script>
