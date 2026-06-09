<template>
  <div>
    <VolTable :columns="columns" :api="langApi" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { langApi } from '@/api/sys/lang'

const { t } = useI18n()

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'langKey', label: t('lang.langKey'), required: true },
  { prop: 'zhCN', label: t('lang.zhCN') },
  { prop: 'zhTW', label: t('lang.zhTW'), tableHidden: true },
  { prop: 'en', label: t('lang.en') },
  { prop: 'ja', label: t('lang.ja'), tableHidden: true },
  { prop: 'ko', label: t('lang.ko'), tableHidden: true },
])
</script>
