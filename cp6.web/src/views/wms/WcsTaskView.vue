<template>
  <div class="wms-wcs">
    <el-card shadow="never" class="search-card">
      <el-form :model="query" inline size="small">
        <el-form-item :label="t('wms.wcs.fld.no')"><el-input v-model="query.taskNo" clearable style="width: 180px" /></el-form-item>
        <el-form-item :label="t('wms.wcs.fld.type')">
          <el-select v-model="query.taskType" clearable style="width: 120px">
            <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.wcs.fld.device')"><el-input v-model="query.deviceCd" clearable style="width: 120px" /></el-form-item>
        <el-form-item :label="t('wms.common.status')">
          <el-select v-model="query.status" clearable style="width: 130px">
            <el-option v-for="(l, v) in statusMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="reload" :loading="loading">{{ t('wms.common.search') }}</el-button>
          <el-button @click="openCreate">{{ t('wms.common.create') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="rows" border stripe size="small" max-height="650" highlight-current-row>
        <el-table-column prop="taskNo" :label="t('wms.wcs.fld.no')" width="170" />
        <el-table-column :label="t('wms.common.status')" width="110">
          <template #default="{ row }"><el-tag :type="statusTag(row.status)" size="small">{{ statusMap[row.status] }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('wms.wcs.fld.type')" width="100">
          <template #default="{ row }">{{ typeMap[row.taskType] || row.taskType }}</template>
        </el-table-column>
        <el-table-column prop="priority" :label="t('wms.wcs.fld.priority')" width="80" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.priority === 3" size="small" type="danger">急</el-tag>
            <el-tag v-else-if="row.priority === 2" size="small" type="warning">↑</el-tag>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="deviceCd" :label="t('wms.wcs.fld.device')" width="100" />
        <el-table-column :label="t('wms.wcs.fld.from')" width="160">
          <template #default="{ row }">{{ row.fromWarehouseCd || '' }}/{{ row.fromLocationCd || '' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.wcs.fld.to')" width="160">
          <template #default="{ row }">{{ row.toWarehouseCd || '' }}/{{ row.toLocationCd || '' }}</template>
        </el-table-column>
        <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
        <el-table-column prop="qty" :label="t('wms.common.qty')" width="100" align="right">
          <template #default="{ row }">{{ row.qty != null ? Number(row.qty).toLocaleString() : '' }}</template>
        </el-table-column>
        <el-table-column prop="relatedNo" :label="t('wms.wcs.fld.related')" width="160" />
        <el-table-column prop="createdAt" :label="t('wms.wcs.fld.created')" width="160" />
        <el-table-column prop="completedAt" :label="t('wms.wcs.fld.completed')" width="160" />
        <el-table-column :label="t('wms.common.action')" width="280" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" link type="primary" size="small" @click="openDispatch(row)">{{ t('wms.wcs.btn.dispatch') }}</el-button>
            <el-button v-if="row.status === 1" link type="warning" size="small" @click="onStart(row)">{{ t('wms.wcs.btn.start') }}</el-button>
            <el-button v-if="row.status === 2" link type="success" size="small" @click="onComplete(row)">{{ t('wms.wcs.btn.complete') }}</el-button>
            <el-button v-if="row.status === 1 || row.status === 2" link type="danger" size="small" @click="openFail(row)">{{ t('wms.wcs.btn.fail') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建 Dialog -->
    <el-dialog v-model="createDialog" :title="t('wms.wcs.dlg.create')" width="600">
      <el-form v-if="editing" :model="editing" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item :label="t('wms.wcs.fld.type')" required>
            <el-select v-model="editing.taskType">
              <el-option v-for="(l, v) in typeMap" :key="v" :label="l" :value="v" />
            </el-select>
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.wcs.fld.priority')">
            <el-input-number v-model="editing.priority" :min="1" :max="3" controls-position="right" style="width: 100%" />
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.wcs.fld.related')">
            <el-input v-model="editing.relatedNo" maxlength="25" />
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'Related Type'">
            <el-input v-model="editing.relatedType" maxlength="20" />
          </el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'From WH'"><el-input v-model="editing.fromWarehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'From Loc'"><el-input v-model="editing.fromLocationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'To WH'"><el-input v-model="editing.toWarehouseCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'To Loc'"><el-input v-model="editing.toLocationCd" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.product')"><el-input v-model="editing.productCd" maxlength="20" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.lot')"><el-input v-model="editing.lotNo" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="t('wms.common.qty')"><el-input-number v-model="editing.qty" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item :label="'Unit'"><el-input v-model="editing.unitCd" maxlength="10" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item :label="t('wms.common.remarks')"><el-input v-model="editing.remarks" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onCreate" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>

    <!-- 派発 Dialog -->
    <el-dialog v-model="dispatchDialog" :title="t('wms.wcs.dlg.dispatch') + ' — ' + dispatchTarget?.taskNo" width="420">
      <el-form label-width="120px" size="small">
        <el-form-item :label="t('wms.wcs.fld.device')" required>
          <el-input v-model="dispatchDevice" maxlength="20" placeholder="AGV01 / CONV-A / ..." />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dispatchDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onDispatch">{{ t('wms.wcs.btn.dispatch') }}</el-button>
      </template>
    </el-dialog>

    <!-- 失败 Dialog -->
    <el-dialog v-model="failDialog" :title="t('wms.wcs.dlg.fail') + ' — ' + failTarget?.taskNo" width="420">
      <el-form label-width="120px" size="small">
        <el-form-item :label="t('wms.wcs.fld.error')" required>
          <el-input v-model="failError" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="failDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="danger" @click="onFail">{{ t('wms.wcs.btn.fail') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { wcsApi } from '@/api/wms/connectivity'
import type { WcsTask, WcsTaskSearchQuery } from '@/types/wms'

const { t } = useI18n()
const query = reactive<WcsTaskSearchQuery>({ pageSize: 100 })
const rows = ref<WcsTask[]>([])
const loading = ref(false)
const saving = ref(false)

const createDialog = ref(false)
const editing = ref<any>(null)

const dispatchDialog = ref(false)
const dispatchTarget = ref<WcsTask | null>(null)
const dispatchDevice = ref('')

const failDialog = ref(false)
const failTarget = ref<WcsTask | null>(null)
const failError = ref('')

const typeMap = computed<Record<string, string>>(() => ({
  MOVE: t('wms.wcs.type.move'),
  PICK: t('wms.wcs.type.pick'),
  PUT: t('wms.wcs.type.put'),
  COUNT: t('wms.wcs.type.count'),
}))
const statusMap = computed<Record<number, string>>(() => ({
  0: t('wms.wcs.status.created'),
  1: t('wms.wcs.status.dispatched'),
  2: t('wms.wcs.status.executing'),
  3: t('wms.wcs.status.completed'),
  9: t('wms.wcs.status.failed'),
}))

function statusTag(s: number): 'info' | 'warning' | 'primary' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'warning', 2: 'primary', 3: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}

async function reload() {
  loading.value = true
  try { rows.value = (await wcsApi.search(query)).data || [] }
  finally { loading.value = false }
}

function openCreate() {
  editing.value = { taskType: 'MOVE', priority: 1, qty: 0 }
  createDialog.value = true
}

async function onCreate() {
  saving.value = true
  try {
    const res = await wcsApi.create(editing.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.taskNo}`)
    createDialog.value = false
    await reload()
  } finally { saving.value = false }
}

function openDispatch(row: WcsTask) {
  dispatchTarget.value = row
  dispatchDevice.value = ''
  dispatchDialog.value = true
}

async function onDispatch() {
  if (!dispatchTarget.value || !dispatchDevice.value) { ElMessage.warning(t('wms.common.required')); return }
  await wcsApi.dispatch(dispatchTarget.value.taskNo, dispatchDevice.value)
  ElMessage.success(t('wms.common.success'))
  dispatchDialog.value = false
  await reload()
}

async function onStart(row: WcsTask) {
  await wcsApi.start(row.taskNo)
  ElMessage.success(t('wms.common.success'))
  await reload()
}

async function onComplete(row: WcsTask) {
  await wcsApi.complete(row.taskNo)
  ElMessage.success(t('wms.common.success'))
  await reload()
}

function openFail(row: WcsTask) {
  failTarget.value = row
  failError.value = ''
  failDialog.value = true
}

async function onFail() {
  if (!failTarget.value || !failError.value) return
  await wcsApi.fail(failTarget.value.taskNo, failError.value)
  ElMessage.success(t('wms.common.success'))
  failDialog.value = false
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.wms-wcs { padding: 16px; }
.search-card { margin-bottom: 12px; }
</style>
