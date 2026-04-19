<template>
  <div>
    <!-- 类型列表模式 -->
    <div v-if="!currentTypeCode">
      <VolTable :columns="typeColumns" :api="dictTypeApi">
        <template #extra-actions="{ row }">
          <el-button link type="success" @click="enterDataMode(row)">{{ t('dict.manageData') }}</el-button>
        </template>
      </VolTable>
    </div>

    <!-- 字典数据模式 -->
    <div v-else>
      <div style="margin-bottom: 12px">
        <el-button :icon="Back" @click="currentTypeCode = ''">{{ t('dict.backToTypes') }}</el-button>
        <el-tag type="info" size="large" style="margin-left: 12px">{{ currentTypeName }}</el-tag>
      </div>
      <VolTable :key="currentTypeCode" :columns="dataColumns" :api="dataApiWrapper" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Back } from '@element-plus/icons-vue'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { dictTypeApi, dictDataApi } from '@/api/dict'

const { t } = useI18n()

const currentTypeCode = ref('')
const currentTypeName = ref('')

// 字典类型列配置
const typeColumns = computed<ColumnConfig[]>(() => [
  { prop: 'typeCode', label: t('dict.typeCode'), required: true },
  { prop: 'typeName', label: t('dict.typeName'), required: true },
  { prop: 'orderNo', label: t('dict.orderNo'), width: 80 },
  { prop: 'enable', label: t('dict.enable'), width: 80, type: 'switch', formType: 'switch' },
])

// 字典数据列配置
const dataColumns = computed<ColumnConfig[]>(() => [
  { prop: 'value', label: t('dict.value'), required: true },
  { prop: 'label', label: t('dict.label'), required: true },
  { prop: 'orderNo', label: t('dict.orderNo'), width: 80 },
  { prop: 'enable', label: t('dict.enable'), width: 80, type: 'switch', formType: 'switch' },
])

// 进入字典数据模式
function enterDataMode(row: any) {
  currentTypeCode.value = row.typeCode
  currentTypeName.value = `${row.typeName} (${row.typeCode})`
}

// 包装 dictDataApi，自动注入 typeCode
const dataApiWrapper = computed(() => ({
  getList(params: any) {
    return dictDataApi.getList({ ...params, typeCode: currentTypeCode.value })
  },
  add(data: any) {
    return dictDataApi.add({ ...data, typeCode: currentTypeCode.value })
  },
  update(data: any) {
    return dictDataApi.update(data)
  },
  del(ids: number[]) {
    return dictDataApi.del(ids)
  }
}))
</script>
