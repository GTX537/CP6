<template>
  <div class="work-order-entry">
    <el-alert v-if="intent.error.value" :title="intent.error.value" type="warning" :closable="false" />
    <el-alert v-if="creationError" :title="creationError" type="warning" :closable="false" />
    <el-card v-if="!isEdit || creationPending || creationResult" shadow="never" style="margin-bottom: 16px">
      <div v-if="!isEdit">
        <template v-if="form.webOrderNo">{{ t('関連受注の現在の権限範囲を使用します。') }}</template>
        <template v-else>
          <span>{{ t('担当部門') }}</span>
          <el-select v-model="creationDepartment" clearable :disabled="saving || !!creationPending" :placeholder="t('担当部門を選択してください')">
            <el-option v-for="department in creationContext?.departments ?? []" :key="department.id" :value="department.id" :label="department.code + ' ' + department.name" />
          </el-select>
          <span v-if="creationContext?.allowsUnbound">{{ t('現在の権限では部門未指定でも作成できます。') }}</span>
        </template>
        <el-button v-if="permissions.has('mes-work-order:add')" :disabled="saving" @click="loadCreationContext">{{ t('作成条件を更新') }}</el-button>
      </div>
      <p v-if="creationPending">{{ t('前回の作成結果を確認してください。確認が終わるまで新規作成はできません。') }}</p>
      <p v-if="creationResult?.status === 'COMMITTED'">{{ t('作成済み') }}: {{ creationResult.result?.workOrderNo }}</p>
      <p v-if="creationResult?.status === 'CLOSED'">{{ t('未完了の操作を閉じました。内容を確認して新規作成できます。') }}</p>
      <el-button v-if="permissions.has('mes-work-order:query')" :disabled="saving" @click="loadCreationScope">{{ t('操作状態を更新') }}</el-button>
      <template v-if="creationPending && permissions.has('mes-work-order:query')">
        <el-button :disabled="saving" @click="resolveCreation(false)">{{ t('前回の結果を確認') }}</el-button>
        <el-button :disabled="saving" @click="resolveCreation(true)">{{ t('未完了の操作を閉じる') }}</el-button>
      </template>
      <el-button v-if="recoveredOnly && permissions.has('mes-work-order:query')" :disabled="saving" @click="loadByNo(form.workOrderNo)">{{ t('作成済み指図を読み込む') }}</el-button>
    </el-card>
    <el-card shadow="never">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <div>
            <el-tag size="large">{{ isEdit ? t('訂正') : t('新規') }}</el-tag>
            <span style="margin-left: 12px; font-weight: 600;">
              {{ t('製造指図入力') }} (MSBBME020)
              <span v-if="form.workOrderNo" style="margin-left: 12px; color: #409EFF;">
                {{ form.workOrderNo }}
              </span>
            </span>
            <el-tag v-if="form.workOrderNo" style="margin-left: 12px;"
              :color="getStatusColor(form.status)" effect="dark" size="small">
              {{ getStatusLabel(form.status) }}
            </el-tag>
          </div>
          <div>
            <el-button @click="$router.back()">{{ t('戻る') }}</el-button>
          </div>
        </div>
      </template>

      <!-- ステップバー -->
      <el-steps :active="step" finish-status="success" align-center style="margin-bottom: 20px;">
        <el-step :title="t('指図基本情報')" :description="t('第1页：基本情報')" />
        <el-step :title="t('工程計画')" :description="t('第2页：工程一覧')" />
        <el-step :title="t('材料手配 + 保存')" :description="t('第3页：材料一覧')" />
      </el-steps>

      <!-- 第1页 -->
      <WoStep1BasicInfo
        v-show="step === 0"
        :model-value="form"
        @update:model-value="value => Object.assign(form, value)"
        :is-edit="isEdit"
        @expand-from-order="onExpandFromOrder"
      />
      <!-- 第2页 -->
      <WoStep2ProcessPlan v-show="step === 1" v-model="form.processes" :production-qty="form.productionQty" />
      <!-- 第3页 -->
      <WoStep3MaterialConfirm
        v-show="step === 2"
        v-model="form.materials"
        :production-qty="form.productionQty"
      />

      <div style="margin-top: 24px; text-align: right;">
        <el-button v-if="step > 0" @click="step--">← {{ t('戻る') }}</el-button>
        <el-button v-if="step < 2" type="primary" @click="next">{{ t('次画面') }} →</el-button>
        <template v-if="step === 2">
          <el-button v-if="isEdit" v-permission="'mes-work-order:edit'" type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
          <el-button v-else v-permission="'mes-work-order:add'" type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
          <el-button v-if="canIssue" v-permission="'mes-work-order:issue'" type="success" :loading="saving" @click="onIssue">{{ t('指図発行') }}</el-button>
        </template>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, computed, watch } from 'vue'
import { usePermissionStore } from '@/stores/permission'
import { pendingWorkOrderCreation, saveWorkOrderCreation, resolveWorkOrderCreation, workOrderResponse } from '@/auth/workOrderCreationRecovery'
import type { WorkOrderCreationContext, WorkOrderCreationScope, WorkOrderCreationOperation } from '@/types/mes/workOrderCreation'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { workOrderApi } from '@/api/mes/mes'
import { WORK_ORDER_STATUS_OPTIONS, type WorkOrderDto } from '@/types/mes/mes'
import WoStep1BasicInfo from './work-order/steps/WoStep1BasicInfo.vue'
import WoStep2ProcessPlan from './work-order/steps/WoStep2ProcessPlan.vue'
import WoStep3MaterialConfirm from './work-order/steps/WoStep3MaterialConfirm.vue'

const permissions = usePermissionStore()
const creationContext = ref<WorkOrderCreationContext | null>(null), creationScope = ref<WorkOrderCreationScope | null>(null)
const creationDepartment = ref<string | null>(null), creationPending = ref<string | null>(null), creationError = ref('')
const creationResult = ref<WorkOrderCreationOperation | null>(null), recoveredOnly = ref(false)
let creationReads = 0
function clearCreation() {
  creationReads++; creationContext.value = null; creationScope.value = null; creationDepartment.value = null
  creationPending.value = null; creationError.value = ''; creationResult.value = null; recoveredOnly.value = false
}
const route = useRoute()
const router = useRouter()
const step = ref(0)


const emptyForm = (): WorkOrderDto => ({
  workOrderNo: '',
  status: 0,
  orderNo1: '',
  orderNo2: '',
  orderNo3: '',
  webOrderNo: '',
  customerCd: '',
  productCd: '',
  productName: '',
  productionQty: 0,
  completedQty: 0,
  defectQty: 0,
  deliveryDate: null,
  planStartDate: null,
  planEndDate: null,
  priority: 1,
  lotNo: '',
  baseCd: '',
  remarks: '',
  processCount: 0,
  completedProcessCount: 0,
  progressRate: 0,
  delayDays: 0,
  processes: [],
  materials: [],
})

const form = reactive<WorkOrderDto>(emptyForm())
let reads = 0, revision = 0
watch(form, () => { revision++ }, { deep: true, flush: 'sync' })
const intent = useOperationsIntent(() => { reads++; clearCreation(); Object.assign(form, emptyForm()); step.value = 0 })
const saving = intent.busy

const isEdit = computed(() => !!form.workOrderNo)
const canIssue = computed(() => !recoveredOnly.value && isEdit.value && (form.status === 0 || form.status === 1))

function getStatusLabel(v: number) {
  return t(WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.label ?? `${v}`)
}
function getStatusColor(v: number) {
  return WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.color ?? '#909399'
}

async function loadByNo(no: string) {
  const token = intent.capture(), id = ++reads, edit = revision
  try {
    const res = await workOrderApi.get(no)
    if (!intent.current(token) || id !== reads || edit !== revision) return
    if (res.code === 0 && res.data) { Object.assign(form, emptyForm(), res.data); recoveredOnly.value = false }
  } catch {
    if (intent.current(token) && id === reads) ElMessage.warning(t('読込できませんでした。保存結果は一覧で確認してください。'))
  }
}

async function onExpandFromOrder(webOrderNo: string) {
  if (creationPending.value) { creationError.value = t('先に前回の作成結果を確認してください。'); return }
  const token = intent.begin(); if (!token) return
  try {
    await ElMessageBox.confirm(t('受注 {webOrderNo} から製造指図を自動展開します。よろしいですか？', { webOrderNo }), t('確認'), { type: 'info' })
    if (!intent.current(token)) return
    const res = await workOrderApi.expandFromOrder({ webOrderNo, priority: 1 })
    if (!intent.current(token)) return
    const nos = res.data?.workOrderNos || []
    ElMessage.success(t('{count}件の指図を作成しました：{nos}', { count: nos.length, nos: nos.join(', ') }))
    if (nos[0]) { form.workOrderNo = nos[0]; await loadByNo(nos[0]) }
  } catch (e) { intent.failed(token, e) } finally { intent.finish(token) }
}

function validateStep1(): boolean {
  if (!isEdit.value && !form.orderNo1 && !form.orderNo2 && !form.orderNo3 && !form.webOrderNo && !form.productCd) {
    ElMessage.error(t('ME-MSG-001: 手配NO or 製品CD が未入力です'))
    return false
  }
  if (form.productionQty <= 0) {
    ElMessage.error(t('ME-MSG-002: 生産数量は0より大きい値を入力してください'))
    return false
  }
  if (form.planStartDate && form.planEndDate && form.planStartDate > form.planEndDate) {
    ElMessage.error(t('ME-MSG-003: 計画開始日は計画完了日以前を指定してください'))
    return false
  }
  if (form.planEndDate && form.deliveryDate && form.planEndDate > form.deliveryDate) {
    ElMessageBox.confirm(t('ME-MSG-004: 計画完了日が客先納期を超えています。よろしいですか？'), t('警告'), {
      type: 'warning',
    }).then(() => { step.value = 1 }).catch(() => {})
    return false
  }
  return true
}

function validateStep2(): boolean {
  if (!form.processes || form.processes.length === 0) {
    ElMessage.error(t('ME-MSG-006: 登録する工程がありません'))
    return false
  }
  // 重複チェック
  const keys = new Set<string>()
  for (const p of form.processes) {
    const k = `${p.processCd}|${p.taskCd}`
    if (keys.has(k)) {
      ElMessage.error(t('ME-MSG-007: 同一の工程コード・作業CDが含まれます'))
      return false
    }
    keys.add(k)
  }
  return true
}

function next() {
  if (step.value === 0 && !validateStep1()) return
  if (step.value === 1 && !validateStep2()) return
  step.value += 1
}

watch(() => form.webOrderNo, () => { creationDepartment.value = null }, { flush: 'sync' })
function creationGuard(token: ReturnType<typeof intent.capture>) {
  if (!intent.current(token)) throw new Error('現在のユーザーまたは画面が変わりました。')
}
async function loadCreationContext() {
  const token = intent.capture(), id = ++creationReads
  try {
    const context = workOrderResponse(await workOrderApi.creationContext())
    if (!intent.current(token) || id !== creationReads) return
    creationContext.value = context; creationScope.value = context; creationDepartment.value = null
    creationPending.value = pendingWorkOrderCreation(context); creationError.value = ''
  } catch (e) { if (intent.current(token) && id === creationReads) creationFailure(token, e) }
}
async function loadCreationScope() {
  const token = intent.capture(), id = ++creationReads
  try {
    const scope = workOrderResponse(await workOrderApi.creationScope())
    if (!intent.current(token) || id !== creationReads) return
    creationScope.value = scope; creationPending.value = pendingWorkOrderCreation(scope); creationError.value = ''
  } catch (e) { if (intent.current(token) && id === creationReads) creationFailure(token, e) }
}
function creationFailure(token: ReturnType<typeof intent.capture>, failure: any) {
  if (!intent.current(token)) return
  const status = failure?.response?.status
  if (status === 401 || status === 403) {
    intent.invalidate(); clearCreation(); reads++; Object.assign(form, emptyForm()); step.value = 0
  }
  creationError.value = t('作成結果を確認できません。再送せず、現在の権限で前回の操作結果を確認してください。')
}
async function resolveCreation(close: boolean) {
  const scope = creationScope.value; if (!scope) return
  const token = intent.begin(); if (!token) return
  try {
    const result = await resolveWorkOrderCreation(scope, close, () => creationGuard(token))
    if (!intent.current(token)) return
    creationResult.value = result; creationPending.value = pendingWorkOrderCreation(scope); creationError.value = ''
    if (result.status === 'COMMITTED' && result.result) {
      Object.assign(form, emptyForm(), { workOrderNo: result.result.workOrderNo }); recoveredOnly.value = true; step.value = 0
      await loadByNo(result.result.workOrderNo)
    }
  } catch (e) { creationFailure(token, e) } finally { intent.finish(token) }
}
async function onSave() {
  if (recoveredOnly.value) { creationError.value = t('作成済み指図を読み込んでから操作してください。'); return }
  if (!validateStep1()) { step.value = 0; return }
  if (!validateStep2()) { step.value = 1; return }
  const draftIdentity = intent.capture()
  const draft = JSON.parse(JSON.stringify(form)) as WorkOrderDto, edit = revision
  const context = creationContext.value, department = draft.webOrderNo ? null : creationDepartment.value
  if (!draft.workOrderNo && (!context || creationPending.value || (!draft.webOrderNo && !context.allowsUnbound && !department))) {
    creationError.value = t('作成条件と担当部門、前回の操作状態を確認してください。'); return
  }
  const token = intent.begin(); if (!token) return
  // begin can discover an identity change before its browser event and clear this draft.
  if (!intent.current(draftIdentity)) { intent.finish(token); return }
  try {
    if (draft.workOrderNo) await workOrderApi.update(draft.workOrderNo, draft)
    else {
      const result = await saveWorkOrderCreation(context!, () => creationGuard(token), operationId => workOrderApi.createOperation({
        operationId, expectedAuthorityVersion: context!.authorityVersion, departmentId: department, workOrder: draft,
      }))
      if (!intent.current(token)) return
      creationResult.value = result; creationPending.value = pendingWorkOrderCreation(context!); creationError.value = ''
      if (result.result) form.workOrderNo = result.result.workOrderNo
    }
    if (!intent.current(token)) return
    ElMessage.success(t('ME-MSG-041: 保存が完了しました'))
    if (draft.workOrderNo && revision === edit) await loadByNo(draft.workOrderNo)
  } catch (e) {
    if (draft.workOrderNo) intent.failed(token, e)
    else {
      if (intent.current(token) && context) creationPending.value = pendingWorkOrderCreation(context)
      creationFailure(token, e)
    }
  } finally { intent.finish(token) }
}

async function onIssue() {
  if (recoveredOnly.value) return
  const token = intent.begin(); if (!token) return
  const no = form.workOrderNo
  try {
    await ElMessageBox.confirm(t('指図を発行します。発行後は基本情報・工程・材料の編集ができません。よろしいですか？'), t('確認'), { type: 'warning' })
    if (!intent.current(token) || no !== form.workOrderNo) return
    await workOrderApi.issue(no)
    if (!intent.current(token)) return
    form.status = 2; ElMessage.success(t('指図を発行しました')); await loadByNo(no)
  } catch (e) { intent.failed(token, e) } finally { intent.finish(token) }
}

watch(() => route.query.no, async (no) => {
  intent.invalidate(); reads++; clearCreation(); Object.assign(form, emptyForm())
  if (typeof no === 'string' && no) await loadByNo(no)
  else if (permissions.has('mes-work-order:add')) await loadCreationContext()
  else if (permissions.has('mes-work-order:query')) await loadCreationScope()
}, { immediate: true })
</script>

<style scoped>
.work-order-entry {
  padding: 16px;
}
</style>
