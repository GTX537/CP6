<template>
  <div class="step3">
    <!-- 顶部：計算ボタン + 見積区分 -->
    <el-card shadow="never" class="calc-bar">
      <el-row :gutter="12" align="middle">
        <el-col :span="6">
          <el-form-item :label="t('見積区分')" label-width="90">
            <el-select v-model="form.qtnDiv" :disabled="isPageReadOnly" clearable size="small">
              <el-option value="A" :label="t('A 積算')" />
              <el-option value="B" :label="t('B 実績')" />
              <el-option value="C" :label="t('C 概算')" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="18" class="right-btns">
          <el-button type="primary" :icon="Refresh" :loading="calculating" :disabled="isPageReadOnly" @click="runCalc">
            再計算
          </el-button>
        </el-col>
      </el-row>
    </el-card>

    <!-- 計算摘要 -->
    <el-card shadow="never" class="summary-card">
      <template #header>
        <span class="title">計算結果</span>
      </template>
      <el-descriptions :column="4" border size="small">
        <el-descriptions-item :label="t('見積面積(m²)')">{{ fmtNum(result.estimateSqm, 4) }}</el-descriptions-item>
        <el-descriptions-item :label="t('標準原価')">{{ fmtMoney(result.standardUnitPrice) }}</el-descriptions-item>
        <el-descriptions-item :label="t('見積単価')">
          <span class="highlight">{{ fmtMoney(result.estimateUnitPrice) }}</span>
        </el-descriptions-item>
        <el-descriptions-item :label="t('確定単価')">
          <el-input-number
            v-model="form.confirmedUnitPrice"
            :min="0"
            :precision="3"
            :controls="false"
            size="small"
            :disabled="isPageReadOnly"
            style="width: 140px"
          />
        </el-descriptions-item>
      </el-descriptions>
    </el-card>

    <!-- 見積り数量×単価×合计 -->
    <el-card shadow="never" class="amount-card">
      <template #header>
        <span class="title">見積り数量別金額</span>
      </template>
      <el-table :data="result.amountRows" size="small" border stripe style="width: 100%">
        <el-table-column prop="index" label="#" width="60" align="center" />
        <el-table-column prop="qty" :label="t('数量')" align="right">
          <template #default="{ row }">{{ fmtNum(row.qty, 0) }}</template>
        </el-table-column>
        <el-table-column prop="unitPrice" :label="t('単価')" align="right">
          <template #default="{ row }">{{ fmtMoney(row.unitPrice) }}</template>
        </el-table-column>
        <el-table-column prop="amount" :label="t('金額')" align="right">
          <template #default="{ row }">{{ fmtMoney(row.amount) }}</template>
        </el-table-column>
      </el-table>
      <div v-if="!result.amountRows.length" class="empty">
        <el-empty description="未計算、または 見積り数量が未入力" :image-size="60" />
      </div>
    </el-card>

    <!-- 計算ロジック透明化 -->
    <el-card shadow="never" class="notes-card">
      <template #header>
        <span class="title">計算ロジック</span>
      </template>
      <ul class="notes">
        <li v-for="(line, i) in result.notes" :key="i">{{ line }}</li>
        <li v-if="!result.notes.length" style="color:#999">未計算</li>
      </ul>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { useEstimateStore } from '@/stores/estimate'
import { useFieldControl } from '@/composables/useFieldControl'
import { estimateCalcApi } from '@/api/erp/estimateCalc'
import type { EstimateCalcResult } from '@/types/erp/estimateCalc'

const store = useEstimateStore()
const { basicInfo } = storeToRefs(store)
const { isPageReadOnly } = useFieldControl()

const form = basicInfo
const calculating = ref(false)

const result = ref<EstimateCalcResult>({
  estimateSqm: 0,
  standardUnitPrice: 0,
  estimateUnitPrice: 0,
  confirmedUnitPrice: 0,
  amountRows: [],
  notes: [],
})

function fmtNum(v?: number, digits = 2) {
  return v == null ? '0' : v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })
}
function fmtMoney(v?: number) {
  return v == null ? '¥0.00' : '¥' + v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

async function runCalc() {
  try {
    calculating.value = true
    const dto = { ...form.value, processes: store.processRows }
    const res = await estimateCalcApi.calculate(dto)
    if (res.code === 0) {
      result.value = res.data
      // 回填主档
      form.value.estimateSqm = res.data.estimateSqm
      form.value.standardUnitPrice = res.data.standardUnitPrice
      form.value.estimateUnitPrice = res.data.estimateUnitPrice
      if (form.value.confirmedUnitPrice == null || form.value.confirmedUnitPrice === 0) {
        form.value.confirmedUnitPrice = res.data.confirmedUnitPrice
      }
      store.calcResult = res.data as any
      ElMessage.success('計算完了')
    }
  } finally {
    calculating.value = false
  }
}

// 进入 Step3 即自动计算一次
onMounted(() => {
  runCalc()
})
</script>

<style scoped>
.step3 {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.calc-bar :deep(.el-card__body),
.summary-card :deep(.el-card__body),
.amount-card :deep(.el-card__body),
.notes-card :deep(.el-card__body) {
  padding: 12px 16px;
}
.title {
  font-weight: 600;
  color: #409eff;
}
.right-btns {
  text-align: right;
}
.highlight {
  font-size: 16px;
  font-weight: 700;
  color: #f56c6c;
}
.notes {
  margin: 0;
  padding-left: 20px;
  font-family: Consolas, 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.8;
  color: #666;
}
.notes li {
  list-style: decimal;
}
.empty {
  padding: 8px 0;
}
</style>
