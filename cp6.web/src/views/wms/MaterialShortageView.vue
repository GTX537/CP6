<template>
  <div class="material-shortage">
    <div class="page-header">
      <h2>{{ t('wms.materialShortage.title') }}</h2>
    </div>

    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.materialShortage.search.workOrderNo')">
          <el-input v-model="query.workOrderNo" clearable style="width: 180px" />
        </el-form-item>
        <el-form-item :label="t('wms.materialShortage.search.status')">
          <el-select v-model="query.status" clearable style="width: 150px">
            <el-option :label="t('wms.materialShortage.status.ALL')" value="" />
            <el-option :label="t('wms.materialShortage.status.OPEN')" value="OPEN" />
            <el-option :label="t('wms.materialShortage.status.RESOLVED')" value="RESOLVED" />
            <el-option :label="t('wms.materialShortage.status.DISMISSED')" value="DISMISSED" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="search">
            {{ t('wms.materialShortage.search.btnSearch') }}
          </el-button>
          <el-button @click="resetQuery">{{ t('wms.materialShortage.search.btnReset') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-row :gutter="12" class="kpi-row">
      <el-col :xs="24" :sm="8" :md="6">
        <el-card shadow="never" class="kpi-card" :class="{ danger: openCount > 0 }">
          <div class="kpi-label">{{ t('wms.materialShortage.kpi.openCount') }}</div>
          <div class="kpi-value">{{ openCount }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-tag size="small">{{ t('wms.common.total') }}: {{ total }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="detectedAt" :label="t('wms.materialShortage.col.detectedAt')" width="170">
          <template #default="{ row }">{{ formatDateTime(row.detectedAt) }}</template>
        </el-table-column>
        <el-table-column prop="workOrderNo" :label="t('wms.materialShortage.col.wo')" width="160" show-overflow-tooltip />
        <el-table-column prop="relatedOutboundNo" :label="t('wms.materialShortage.col.outbound')" width="160" show-overflow-tooltip />
        <el-table-column prop="productCd" :label="t('wms.materialShortage.col.product')" width="130" show-overflow-tooltip />
        <el-table-column prop="lotNo" :label="t('wms.materialShortage.col.lot')" width="120" show-overflow-tooltip />
        <el-table-column prop="requiredQty" :label="t('wms.materialShortage.col.requiredQty')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.requiredQty) }}</template>
        </el-table-column>
        <el-table-column prop="availableQty" :label="t('wms.materialShortage.col.availableQty')" width="120" align="right">
          <template #default="{ row }">{{ formatQty(row.availableQty) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.materialShortage.col.shortQty')" width="120" align="right">
          <template #default="{ row }">
            <span class="short-qty">{{ formatQty(shortQty(row)) }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="status" :label="t('wms.materialShortage.col.status')" width="120">
          <template #default="{ row }">
            <el-tag :type="statusTag(row.status)" size="small">
              {{ t(`wms.materialShortage.status.${row.status}`) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="remark" :label="t('wms.materialShortage.col.remark')" min-width="180" show-overflow-tooltip />
        <el-table-column :label="t('wms.materialShortage.col.action')" width="180" fixed="right">
          <template #default="{ row }">
            <el-button
              link
              type="success"
              size="small"
              :disabled="row.status !== 'OPEN'"
              @click="openAction(row, 'resolve')"
            >
              {{ t('wms.materialShortage.btn.resolve') }}
            </el-button>
            <el-button
              link
              type="info"
              size="small"
              :disabled="row.status !== 'OPEN'"
              @click="openAction(row, 'dismiss')"
            >
              {{ t('wms.materialShortage.btn.dismiss') }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[50, 100, 200]"
        layout="total, sizes, prev, pager, next, jumper"
        style="margin-top: 12px; justify-content: flex-end"
        @current-change="reload"
        @size-change="reload"
      />
    </el-card>

    <el-dialog v-model="actionDialogVisible" :title="actionTitle" width="520">
      <el-form label-position="top">
        <el-form-item :label="t('wms.materialShortage.dlg.remarkLabel')">
          <el-input
            v-model="actionRemark"
            type="textarea"
            :rows="4"
            maxlength="500"
            show-word-limit
            :placeholder="t('wms.materialShortage.dlg.remarkPlaceholder')"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="actionDialogVisible = false" :disabled="actionSaving">
          {{ t('wms.materialShortage.btn.cancel') }}
        </el-button>
        <el-button type="primary" :loading="actionSaving" @click="submitAction">
          {{ t('wms.materialShortage.btn.confirm') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { materialShortageApi } from '@/api/wms/materialShortage'
import type { MaterialShortage, MaterialShortageQuery, MaterialShortageStatus } from '@/types/wms/materialShortage'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()

type ActionType = 'resolve' | 'dismiss'

const query = reactive<Required<Pick<MaterialShortageQuery, 'page' | 'pageSize'>> & {
  status: MaterialShortageStatus | ''
  workOrderNo?: string
}>({
  status: 'OPEN',
  page: 1,
  pageSize: 50,
})

const rows = ref<MaterialShortage[]>([])
const total = ref(0)
const openCount = ref(0)
const loading = ref(false)

const actionDialogVisible = ref(false)
const actionSaving = ref(false)
const actionType = ref<ActionType>('resolve')
const actionTarget = ref<MaterialShortage | null>(null)
const actionRemark = ref('')

const actionTitle = computed(() =>
  actionType.value === 'resolve'
    ? t('wms.materialShortage.dlg.resolveTitle')
    : t('wms.materialShortage.dlg.dismissTitle'),
)

function normalizePaged(res: any) {
  const data = res?.data || {}
  return {
    items: (data.items || data.Items || []) as MaterialShortage[],
    total: Number(data.total ?? data.Total ?? 0),
  }
}

function formatQty(n: number | null | undefined): string {
  if (n == null) return ''
  return fmtQty(n, 4)
}

function formatDateTime(value?: string): string {
  return value ? value.replace('T', ' ').slice(0, 19) : ''
}

function shortQty(row: MaterialShortage): number {
  return Math.max(0, Number(row.requiredQty || 0) - Number(row.availableQty || 0))
}

function statusTag(status: MaterialShortageStatus): 'success' | 'danger' | 'info' {
  if (status === 'OPEN') return 'danger'
  if (status === 'RESOLVED') return 'success'
  return 'info'
}

async function reload() {
  loading.value = true
  try {
    const [listRes, openRes] = await Promise.all([
      materialShortageApi.search({
        status: query.status,
        workOrderNo: query.workOrderNo,
        page: query.page,
        pageSize: query.pageSize,
      }),
      materialShortageApi.search({
        status: 'OPEN',
        page: 1,
        pageSize: 1,
      }),
    ])
    const list = normalizePaged(listRes)
    const open = normalizePaged(openRes)
    rows.value = list.items
    total.value = list.total
    openCount.value = open.total
  } finally {
    loading.value = false
  }
}

function search() {
  query.page = 1
  reload()
}

function resetQuery() {
  query.status = 'OPEN'
  query.workOrderNo = undefined
  query.page = 1
  reload()
}

function openAction(row: MaterialShortage, type: ActionType) {
  actionTarget.value = row
  actionType.value = type
  actionRemark.value = ''
  actionDialogVisible.value = true
}

async function submitAction() {
  if (!actionTarget.value) return
  actionSaving.value = true
  try {
    if (actionType.value === 'resolve') {
      await materialShortageApi.resolve(actionTarget.value.id, { remark: actionRemark.value })
      ElMessage.success(t('wms.materialShortage.msg.resolved'))
    } else {
      await materialShortageApi.dismiss(actionTarget.value.id, { remark: actionRemark.value })
      ElMessage.success(t('wms.materialShortage.msg.dismissed'))
    }
    actionDialogVisible.value = false
    await reload()
  } finally {
    actionSaving.value = false
  }
}

onMounted(reload)
</script>

<style scoped>
.material-shortage {
  padding: 12px;
}

.page-header {
  margin-bottom: 12px;
}

.page-header h2 {
  margin: 0;
  color: #303133;
  font-size: 20px;
  font-weight: 650;
  line-height: 1.3;
}

.search-card {
  margin-bottom: 12px;
}

.kpi-row {
  margin-bottom: 12px;
}

.kpi-card {
  border-left: 4px solid #409eff;
  min-height: 84px;
}

.kpi-card.danger {
  border-left-color: #f56c6c;
}

.kpi-label {
  color: #606266;
  font-size: 12px;
  margin-bottom: 8px;
}

.kpi-value {
  color: #303133;
  font-size: 28px;
  font-weight: 650;
  line-height: 1;
}

.table-toolbar {
  margin-bottom: 8px;
}

.short-qty {
  color: #d93026;
  font-weight: 600;
}
</style>
