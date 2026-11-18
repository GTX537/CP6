<template>
  <div>
    <el-tabs v-model="tab">
      <el-tab-pane :label="t('原分配器规则')" name="legacy" />
      <el-tab-pane :label="t('受控编号域')" name="managed" />
    </el-tabs>
    <ManagedNumberRules v-if="tab === 'managed'" />
    <VolTable v-else
      :columns="columns"
      :api="legacyApi"
      add-permission="pub-seq:add"
      edit-permission="pub-seq:edit"
      delete-permission="pub-seq:delete"
    >
      <template #extra-actions="{ row }">
        <el-button link type="primary" @click="preview(row)">{{ t('预览') }}</el-button>
      </template>
    </VolTable>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { seqApi } from '@/api/pub/seq'
import ManagedNumberRules from './ManagedNumberRules.vue'

const { t } = useI18n()

const tab = ref('legacy')
const legacyApi = { ...seqApi, getList: (params: Parameters<typeof seqApi.getList>[0]) => seqApi.getList({ ...params, legacyOnly: true }) }

const resetOptions = [
  { label: t('不重置'), value: 0 },
  { label: t('每日'), value: 1 },
  { label: t('每月'), value: 2 },
  { label: t('每年'), value: 3 },
]

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'bizKey', label: t('业务键'), required: true },
  { prop: 'prefix', label: t('前缀'), required: true },
  { prop: 'dateFormat', label: t('日期格式') },        // 如 yyyyMMdd，空=无日期
  { prop: 'seqLength', label: t('流水位数'), width: 100 },
  { prop: 'resetCycle', label: t('重置周期'), formType: 'select', options: resetOptions },
  { prop: 'currentValue', label: t('当前流水'), width: 100, formType: 'none' },
])

async function preview(row: any) {
  const preview = await seqApi.previewDetails(row.bizKey)
  const note = preview.wouldReset ? t('已跨周期，原示例沿用旧计数。') : t('跨周期后示例可能变化。')
  ElMessage.info(t('格式示例') + ' ' + preview.sample + ' · ' + t('未占号') + '。' + note + ' ' + preview.timeZone)
}
</script>
