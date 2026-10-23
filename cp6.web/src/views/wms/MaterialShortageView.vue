<template>
  <CpPageShell :title="t('wms.materialShortage.title')" :count="result?.total">
    <p>此处更新缺料跟进状态，库存数量和采购申请不会随之改变。</p>
    <p v-if="result">当前制造指图筛选范围内，未处理 {{ result.openCount }} 条。</p>
    <el-form inline @submit.prevent="search">
      <el-form-item :label="t('wms.materialShortage.search.workOrderNo')"><el-input v-model="query.workOrderNo" clearable /></el-form-item>
      <el-form-item :label="t('wms.materialShortage.search.status')">
        <el-select v-model="query.status">
          <el-option label="全部" value="" /><el-option label="未处理" value="OPEN" />
          <el-option label="已解决" value="RESOLVED" /><el-option label="已消除" value="DISMISSED" />
        </el-select>
      </el-form-item>
      <el-button type="primary" :loading="loading" :disabled="!permitted() || intent.busy.value" @click="search">{{ t('wms.materialShortage.search.btnSearch') }}</el-button>
    </el-form>
    <p v-if="readError || intent.error.value" role="alert">{{ readError || intent.error.value }}</p>
    <p v-if="message" role="status">{{ message }}</p>
    <el-table :data="result?.rows ?? []" row-key="id" border v-loading="loading">
      <el-table-column prop="workOrderNo" :label="t('wms.materialShortage.col.wo')" min-width="150" />
      <el-table-column prop="relatedOutboundNo" :label="t('wms.materialShortage.col.outbound')" min-width="150" />
      <el-table-column prop="productCd" :label="t('wms.materialShortage.col.product')" />
      <el-table-column prop="lotNo" :label="t('wms.materialShortage.col.lot')" />
      <el-table-column :label="t('wms.materialShortage.col.requiredQty')" min-width="190"><template #default="{ row }">{{ observationText(row.requiredQty) }}</template></el-table-column>
      <el-table-column :label="t('wms.materialShortage.col.availableQty')" min-width="190"><template #default="{ row }">{{ observationText(row.availableQty) }}</template></el-table-column>
      <el-table-column :label="t('wms.materialShortage.col.shortQty')" min-width="190"><template #default="{ row }">{{ observationText(row.shortQty) }}</template></el-table-column>
      <el-table-column prop="status" :label="t('wms.materialShortage.col.status')" />
      <el-table-column prop="detectedAt" :label="t('wms.materialShortage.col.detectedAt')" min-width="180" />
      <el-table-column v-if="result?.remarkReadable" prop="remark" :label="t('wms.materialShortage.col.remark')" min-width="160" />
      <el-table-column :label="t('wms.materialShortage.col.action')" min-width="150">
        <template #default="{ row }">
          <el-button v-if="actionAllowed('resolve')" link :disabled="!canAct(row)" @click="openAction(row, 'resolve')">{{ t('wms.materialShortage.btn.resolve') }}</el-button>
          <el-button v-if="actionAllowed('dismiss')" link :disabled="!canAct(row)" @click="openAction(row, 'dismiss')">{{ t('wms.materialShortage.btn.dismiss') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-pagination v-if="result" :current-page="result.page" :page-size="result.pageSize" :total="result.total"
      layout="total, prev, pager, next" @current-change="pageChanged" />
    <el-dialog v-model="dialogVisible" title="更新缺料跟进状态" width="520">
      <p>{{ actionTarget?.workOrderNo }} · {{ actionTarget?.productCd }}</p>
      <el-input v-if="result?.remarkWritable" v-model="remark" type="textarea" :rows="4" maxlength="500" show-word-limit />
      <p v-else>本次保留原备注。</p>
      <template #footer>
        <el-button @click="closeDialog">取消</el-button>
        <el-button type="primary" :disabled="!actionAllowed(actionType) || intent.busy.value || intent.uncertain.value" @click="submitAction">确认跟进状态</el-button>
      </template>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { materialShortageApi } from '@/api/wms/materialShortage'
import { materialShortageVersion, type MaterialShortage, type MaterialShortagePaged, type MaterialShortageQuery } from '@/types/wms/materialShortage'
import { observationText } from '@/utils/inventoryObservation'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'

const { t } = useI18n(), permission = usePermissionStore()
const query = reactive<MaterialShortageQuery>({ status: 'OPEN', page: 1, pageSize: 50 })
const result = ref<MaterialShortagePaged | null>(null), loading = ref(false), readError = ref(''), message = ref('')
type Action = 'resolve' | 'dismiss'
const actionTarget = ref<MaterialShortage | null>(null), actionType = ref<Action>('resolve'), dialogVisible = ref(false), remark = ref('')
let sequence = 0, applied: MaterialShortageQuery | null = null
function closeDialog() { dialogVisible.value = false; actionTarget.value = null; remark.value = '' }
function clearProtected() { sequence++; result.value = null; applied = null; loading.value = false; message.value = ''; closeDialog() }
const intent = useOperationsIntent(() => { clearProtected(); readError.value = '身份已变化，请重新查询。' })
const permitted = () => permission.loaded && permission.has('wms-material-shortage:query') && permission.has('mes-work-order:query')
const actionAllowed = (action: Action) => permitted() && permission.has(`wms-material-shortage:${action}`)
watch(permitted, allowed => { if (!allowed) clearProtected() })
watch(() => actionAllowed(actionType.value), allowed => { if (!allowed) closeDialog() })
function canAct(row: MaterialShortage) {
  return row.status === 'OPEN' && !!row.rowVersion && !intent.busy.value && !intent.uncertain.value &&
    !!result.value?.rows.some(current => current.id === row.id && current.rowVersion === row.rowVersion)
}
async function load(input: MaterialShortageQuery) {
  const token = intent.capture(); clearProtected(); const read = sequence
  const current = () => intent.current(token) && read === sequence
  loading.value = true; readError.value = ''
  try {
    if (!permitted()) throw new Error('Current query permission required')
    const response = await materialShortageApi.search({ ...input })
    if (!current()) return
    if (!permitted() || response.code !== 0 || response.data?.version !== materialShortageVersion) throw new Error('Current Owner response required')
    result.value = response.data; applied = { ...input }; intent.uncertain.value = false; intent.error.value = ''
  } catch {
    if (current()) { result.value = null; readError.value = '读取失败，请重新查询当前权限范围。' }
  } finally { if (current()) loading.value = false }
}
function search() { if (!intent.busy.value) void load({ ...query, workOrderNo: query.workOrderNo?.trim() || undefined, page: 1 }) }
function pageChanged(page: number) { if (applied && !intent.busy.value) void load({ ...applied, page }) }
function openAction(row: MaterialShortage, action: Action) {
  if (!actionAllowed(action) || !canAct(row)) return
  actionTarget.value = { ...row }; actionType.value = action
  remark.value = result.value?.remarkWritable ? row.remark ?? '' : ''; dialogVisible.value = true
}
async function submitAction() {
  const target = actionTarget.value, action = actionType.value
  if (!target || !actionAllowed(action) || !canAct(target)) { closeDialog(); return }
  const token = intent.begin(); if (!token) return
  const writable = result.value?.remarkWritable === true
  const request = { rowVersion: target.rowVersion, setRemark: writable, ...(writable ? { remark: remark.value } : {}) }
  const reload = { ...(applied ?? query) }; clearProtected(); const write = sequence
  const current = () => intent.current(token) && write === sequence
  try {
    const response = await materialShortageApi[action](target.id, request)
    if (!current()) return
    if (!actionAllowed(action) || response.code !== 0 || response.data?.id !== target.id ||
      response.data.status !== (action === 'resolve' ? 'RESOLVED' : 'DISMISSED')) throw new Error('Result unavailable')
    await load(reload)
    if (intent.current(token)) message.value = '缺料跟进状态已更新；库存数量未变。'
  } catch (error) {
    if (current()) { intent.failed(token, error); clearProtected() }
  } finally { intent.finish(token) }
}
onMounted(search)
</script>
