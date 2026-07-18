<template>
  <div class="production-result-entry">
    <!-- 上部：指図サマリ -->
    <el-card shadow="never" class="header-card">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <strong>{{ t('製造実績入力') }} (MSBBME040)</strong>
          <el-button @click="$router.back()">{{ t('戻る') }}</el-button>
        </div>
      </template>
      <el-form :inline="true" label-width="100px" size="small">
        <el-form-item :label="t('指図NO')">
          <el-input v-model="workOrderNo" style="width: 200px;" placeholder="WOyyyyMMdd-NNNN" @keyup.enter="loadWorkOrder" />
          <el-button type="primary" :icon="Search" @click="loadWorkOrder" style="margin-left: 8px;">{{ t('取得') }}</el-button>
        </el-form-item>
        <el-form-item :label="t('作業者CD')">
          <el-input v-model="operatorCd" style="width: 150px;" :placeholder="t('必須')" />
        </el-form-item>
      </el-form>
      <div v-if="workOrder" style="margin-top: 8px; display: flex; gap: 16px; flex-wrap: wrap;">
        <el-tag size="large" :color="getStatusColor(workOrder.status)" effect="dark">
          {{ getStatusLabel(workOrder.status) }}
        </el-tag>
        <div><strong>{{ t('製品：') }}</strong>{{ workOrder.productCd }} / {{ workOrder.productName }}</div>
        <div><strong>{{ t('得意先：') }}</strong>{{ workOrder.customerCd }}</div>
        <div><strong>{{ t('生産数量：') }}</strong>{{ workOrder.productionQty }}</div>
        <div><strong>{{ t('完了：') }}</strong>{{ workOrder.completedQty }}</div>
        <div><strong>{{ t('不良：') }}</strong>{{ workOrder.defectQty }}</div>
        <div><strong>{{ t('納期：') }}</strong>{{ formatDate(workOrder.deliveryDate) }}</div>
      </div>
    </el-card>

    <!-- 工程別実績入力 -->
    <el-card v-if="workOrder" shadow="never" style="margin-top: 12px;">
      <template #header>{{ t('工程別実績入力') }}</template>
      <el-table :data="workOrder.processes" border stripe size="small" style="width: 100%;">
        <el-table-column :label="t('順')" width="60" align="center">
          <template #default="{ row }">{{ row.sortOrder }}</template>
        </el-table-column>
        <el-table-column prop="processCd" :label="t('工程CD')" width="110" />
        <el-table-column prop="taskCd" :label="t('作業CD')" width="110" />
        <el-table-column prop="processName" :label="t('工程名')" min-width="140" />
        <el-table-column prop="machineCd" :label="t('号機')" width="110" />
        <el-table-column :label="t('状態')" width="100" align="center">
          <template #default="{ row }">
            <el-tag :color="getProcStatusColor(row.processStatus)" effect="dark" size="small">
              {{ getProcStatusLabel(row.processStatus) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('計画/完了/不良')" width="180" align="center">
          <template #default="{ row }">
            <span>{{ row.planQty || 0 }} / {{ row.goodQty }} / {{ row.defectQty }}</span>
          </template>
        </el-table-column>
        <el-table-column :label="t('実績開始')" width="160">
          <template #default="{ row }">{{ formatDateTime(row.actualStartTime) }}</template>
        </el-table-column>
        <el-table-column :label="t('実績完了')" width="160">
          <template #default="{ row }">{{ formatDateTime(row.actualEndTime) }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="380" align="center" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.processStatus === 0" v-permission="'mes-production-result:start'" size="small" type="primary" @click="openDialog(row, 1)">{{ t('開始') }}</el-button>
            <el-button v-if="row.processStatus === 1" v-permission="'mes-production-result:suspend'" size="small" type="warning" @click="openDialog(row, 2)">{{ t('中断') }}</el-button>
            <el-button v-if="row.processStatus === 3" v-permission="'mes-production-result:suspend'" size="small" type="success" @click="openDialog(row, 3)">{{ t('中断解除') }}</el-button>
            <el-button v-if="row.processStatus === 1" v-permission="'mes-production-result:complete'" size="small" type="success" @click="openDialog(row, 4)">{{ t('完了') }}</el-button>
            <el-button v-if="row.processStatus === 1 || row.processStatus === 2" v-permission="'mes-production-result:report'" size="small" type="info" @click="openDialog(row, 5)">{{ t('数量追加報告') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 実績入力ダイアログ -->
    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="560px" @closed="resetForm">
      <el-form :model="form" label-width="120px" size="small">
        <el-form-item :label="t('工程')">
          <el-tag>{{ form.processCd }} - {{ currentProcessName }}</el-tag>
        </el-form-item>
        <el-form-item :label="t('作業者CD')" required>
          <el-input v-model="form.operatorCd" />
        </el-form-item>
        <el-form-item :label="t('作業者名')">
          <el-input v-model="form.operatorName" />
        </el-form-item>
        <el-form-item v-if="resultType === 1" :label="t('実績開始日時')">
          <el-date-picker v-model="form.actualStartTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%;" />
        </el-form-item>
        <template v-if="resultType === 4 || resultType === 5">
          <el-form-item :label="t('良品数')" required>
            <el-input-number v-model="form.goodQty" :min="0" :precision="0" style="width: 100%;" />
          </el-form-item>
          <el-form-item :label="t('不良数')">
            <el-input-number v-model="form.defectQty" :min="0" :precision="0" style="width: 100%;" />
          </el-form-item>
          <el-form-item v-if="(form.defectQty || 0) > 0" :label="t('不良理由CD')" required>
            <el-input v-model="form.defectReasonCd" :placeholder="t('例: D01')" />
          </el-form-item>
          <el-form-item v-if="resultType === 4" :label="t('完了日時')">
            <el-date-picker v-model="form.actualEndTime" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%;" />
          </el-form-item>
        </template>
        <el-form-item v-if="resultType === 2" :label="t('中断理由CD')" required>
          <el-input v-model="form.suspendReasonCd" :placeholder="t('例: S01')" />
        </el-form-item>
        <el-form-item :label="t('号機')">
          <el-input v-model="form.machineCd" />
        </el-form-item>
        <el-form-item :label="t('備考')">
          <el-input v-model="form.resultNote" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('キャンセル') }}</el-button>
        <el-button type="primary" :loading="submitting" @click="submit">{{ t('確定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Search } from '@element-plus/icons-vue'
import { workOrderApi, productionResultApi } from '@/api/mes/mes'
import {
  WORK_ORDER_STATUS_OPTIONS,
  PROCESS_STATUS_OPTIONS,
  type WorkOrderDto,
  type WorkOrderProcessDto,
  type ProductionResultRequest,
} from '@/types/mes/mes'

const route = useRoute()
const { t } = useI18n()
const workOrderNo = ref('')
const operatorCd = ref('')
const workOrder = ref<WorkOrderDto | null>(null)

const dialogVisible = ref(false)
const submitting = ref(false)
const resultType = ref<1 | 2 | 3 | 4 | 5>(1)
const currentProcessName = ref('')

const form = reactive<ProductionResultRequest>({
  workOrderNo: '',
  processCd: '',
  taskCd: null,
  operatorCd: '',
  operatorName: '',
  actualStartTime: null,
  actualEndTime: null,
  goodQty: 0,
  defectQty: 0,
  defectReasonCd: null,
  suspendReasonCd: null,
  machineCd: null,
  resultNote: null,
})

const dialogTitle = computed(() => {
  const map: Record<number, string> = { 1: t('工程開始'), 2: t('工程中断'), 3: t('中断解除'), 4: t('工程完了'), 5: t('数量追加報告') }
  return map[resultType.value] ?? t('実績入力')
})

function getStatusLabel(v: number) {
  return t(WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.label ?? `${v}`)
}
function getStatusColor(v: number) {
  return WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.color ?? '#909399'
}
function getProcStatusLabel(v: number) {
  return t(PROCESS_STATUS_OPTIONS.find(s => s.value === v)?.label ?? `${v}`)
}
function getProcStatusColor(v: number) {
  return PROCESS_STATUS_OPTIONS.find(s => s.value === v)?.color ?? '#909399'
}
function formatDate(v?: string | null) {
  return v ? v.substring(0, 10) : ''
}
function formatDateTime(v?: string | null) {
  if (!v) return ''
  return v.substring(0, 16).replace('T', ' ')
}

async function loadWorkOrder() {
  if (!workOrderNo.value) {
    ElMessage.warning(t('指図NOを入力してください'))
    return
  }
  const res = await workOrderApi.get(workOrderNo.value)
  if (res.code === 0 && res.data) {
    workOrder.value = res.data
    if (res.data.status < 2) {
      ElMessage.warning(t('発行前の指図です。実績入力できません。'))
    }
  }
}

function openDialog(row: WorkOrderProcessDto, type: 1 | 2 | 3 | 4 | 5) {
  resultType.value = type
  currentProcessName.value = row.processName || ''
  form.workOrderNo = workOrder.value!.workOrderNo
  form.processCd = row.processCd
  form.taskCd = row.taskCd
  form.operatorCd = operatorCd.value || ''
  form.operatorName = ''
  form.actualStartTime = type === 1 ? new Date().toISOString().slice(0, 19) : null
  form.actualEndTime = type === 4 ? new Date().toISOString().slice(0, 19) : null
  form.goodQty = 0
  form.defectQty = 0
  form.defectReasonCd = null
  form.suspendReasonCd = null
  form.machineCd = row.machineCd || null
  form.resultNote = null
  dialogVisible.value = true
}

function resetForm() {
  form.workOrderNo = ''
  form.processCd = ''
}

async function submit() {
  if (!form.operatorCd) {
    ElMessage.error(t('ME-MSG-011: 作業者が未入力です'))
    return
  }
  if ((resultType.value === 4 || resultType.value === 5) && form.goodQty <= 0 && form.defectQty <= 0) {
    ElMessage.error(t('ME-MSG-012: 良品数が未入力です'))
    return
  }
  if ((resultType.value === 4 || resultType.value === 5) && (form.defectQty || 0) > 0 && !form.defectReasonCd) {
    ElMessage.error(t('ME-MSG-014: 不良理由が未選択です'))
    return
  }
  if (resultType.value === 2 && !form.suspendReasonCd) {
    ElMessage.error(t('中断理由を入力してください'))
    return
  }
  submitting.value = true
  try {
    const fn = {
      1: productionResultApi.start,
      2: productionResultApi.suspend,
      3: productionResultApi.resume,
      4: productionResultApi.complete,
      5: productionResultApi.report,
    }[resultType.value]
    const res = await fn(form)
    ElMessage.success(t('ME-MSG-041: 保存しました（{resultNo}）', { resultNo: res.data?.resultNo }))
    dialogVisible.value = false
    await loadWorkOrder()
  } finally {
    submitting.value = false
  }
}

onMounted(async () => {
  const no = route.query.no as string | undefined
  if (no) {
    workOrderNo.value = no
    await loadWorkOrder()
  }
})
</script>

<style scoped>
.production-result-entry {
  padding: 16px;
}
.header-card {
  margin-bottom: 0;
}
</style>
