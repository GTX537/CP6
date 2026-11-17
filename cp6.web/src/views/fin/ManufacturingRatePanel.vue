<template>
  <section class="rate-panel" aria-label="工序费率版本">
    <el-alert v-if="message" :type="messageType" :title="message" :closable="false" show-icon />
    <el-form class="toolbar" :inline="true" @submit.prevent="reload">
      <el-form-item label="工作中心或制品">
        <el-input v-model.trim="search" clearable maxlength="100" aria-label="工作中心或制品" />
      </el-form-item>
      <el-form-item label="候选生效日">
        <el-input v-model="date" type="date" aria-label="候选生效日" />
      </el-form-item>
      <el-button native-type="submit" :loading="loading" :disabled="busy">查询</el-button>
      <el-button v-if="canDraft" type="primary" :disabled="loading || busy || requiresReload || !rows?.choices.length"
        @click="openDraft">新建费率版本</el-button>
    </el-form>
    <p v-if="rows?.truncated" class="limit-note">显示前 100 条，请按工作中心、制品或版本编号缩小查询范围。</p>
    <el-table :data="rows?.versions || []" row-key="rateId" size="small" stripe v-loading="loading">
      <template #empty>当前范围没有费率版本</template>
      <el-table-column prop="wgCd" label="工作中心" min-width="110" />
      <el-table-column label="制品 / 工序" min-width="160">
        <template #default="{ row }">{{ row.productCd }} / {{ row.processCd }} {{ row.taskCd }}</template>
      </el-table-column>
      <el-table-column label="人工费率 / 人时" prop="laborRate" min-width="150" align="right" />
      <el-table-column label="制造费率 / 机时" prop="overheadRate" min-width="150" align="right" />
      <el-table-column label="生效区间" min-width="188">
        <template #default="{ row }">{{ interval(row) }}</template>
      </el-table-column>
      <el-table-column label="状态" min-width="95">
        <template #default="{ row }"><el-tag size="small">{{ stateLabel(row.state) }}</el-tag></template>
      </el-table-column>
      <el-table-column label="操作" min-width="112">
        <template #default="{ row }">
          <el-button v-if="row.state === 'DRAFT' && row.madeByCurrentUser && canDraft" link type="primary"
            :disabled="busy || loading || requiresReload" @click="transition(row, 'submit')">送审</el-button>
          <el-button v-if="row.state === 'SUBMITTED' && !row.madeByCurrentUser && allowed('rate-review')" link type="primary"
            :disabled="busy || loading || requiresReload" @click="transition(row, 'review')">批准</el-button>
          <el-button v-if="row.state === 'APPROVED' && allowed('rate-release')" link type="primary"
            :disabled="busy || loading || requiresReload" @click="preview(row)">发布预览</el-button>
          <el-button link @click="showId = row.rateId">版本编号</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-dialog :model-value="!!showId" title="费率版本编号" width="min(460px, 92vw)" @close="showId = ''">
      <code class="identifier">{{ showId }}</code>
    </el-dialog>

    <el-dialog v-model="draftOpen" title="新建费率版本" width="min(680px, 94vw)" :close-on-click-modal="false">
      <el-form label-position="top" @submit.prevent="saveDraft">
        <el-form-item label="工作中心、工序和批准依据" required>
          <el-select v-model="draft.basisId" filterable aria-label="工作中心、工序和批准依据" @change="draft.centerId = ''">
            <el-option v-for="choice in rows?.choices" :key="choice.id" :value="choice.id"
              :label="`${choice.wgCd} · ${choice.productCd} / ${choice.processCd} ${choice.taskCd} · ${choice.id}`" />
          </el-select>
        </el-form-item>
        <el-form-item label="成本中心版本" required>
          <el-select v-model="draft.centerId" filterable aria-label="成本中心版本" :disabled="!selectedChoice">
            <el-option v-for="center in availableCenters" :key="center.locator.versionId"
              :value="center.locator.versionId" :label="center.code + ' · ' + center.name" />
          </el-select>
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="人工费率 / 人时" required>
            <el-input v-model.trim="draft.laborRate" inputmode="decimal" aria-label="人工费率 / 人时" />
          </el-form-item>
          <el-form-item label="制造费率 / 机时" required>
            <el-input v-model.trim="draft.overheadRate" inputmode="decimal" aria-label="制造费率 / 机时" />
          </el-form-item>
          <el-form-item label="生效日"><span>{{ rows?.businessDate }}</span></el-form-item>
          <el-form-item label="截止日（不含，可留空）">
            <el-input v-model="draft.toExclusive" type="date" aria-label="截止日（不含，可留空）" />
          </el-form-item>
        </div>
        <p v-if="selectedChoice?.zeroAllowed" class="limit-note">所选依据允许有据零费率。</p>
        <el-alert v-if="draftError" type="error" :title="draftError" :closable="false" />
      </el-form>
      <template #footer>
        <el-button :disabled="busy" @click="draftOpen = false">取消</el-button>
        <el-button type="primary" :loading="busy" :disabled="!draftValid || !canDraft || requiresReload"
          @click="saveDraft">保存草稿</el-button>
      </template>
    </el-dialog>

    <el-dialog :model-value="!!release" title="核对费率发布前后的生效区间" width="min(700px, 94vw)"
      :close-on-click-modal="false" @close="release = null">
      <div v-if="release" class="form-grid">
        <div v-for="column in timeline" :key="column.title">
          <h3>{{ column.title }}</h3>
          <p v-if="!column.items.length" class="limit-note">无生效区间</p>
          <ul class="timeline">
            <li v-for="segment in column.items" :key="segment.rateId + segment.from">
              <div>{{ interval(segment) }}</div>
              <el-tag v-if="segment.rateId === release.rateId" size="small">本次版本</el-tag>
              <code v-else class="identifier">{{ segment.rateId }}</code>
            </li>
          </ul>
        </div>
      </div>
      <el-alert v-if="releaseError" type="warning" :title="releaseError" :closable="false" />
      <template #footer>
        <el-button :disabled="busy" @click="release = null">关闭</el-button>
        <el-button v-if="allowed('rate-release')" type="primary" :loading="busy" :disabled="!release"
          @click="publish">{{ releaseError ? '按原预览提交' : '确认发布' }}</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { manufacturingCostApi, maintenanceMessage } from '@/api/fin/manufacturingCost'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'
import type { FinanceContext, RateMaintenance, RatePreview, RateSegment, RateState, RateVersion } from '@/types/fin/manufacturingCost'

const { finance } = defineProps<{ finance: FinanceContext }>()
const permissions = usePermissionStore()
const allowed = (action: string) => permissions.loaded && permissions.has('fin-cost:' + action)
const canRead = computed(() => allowed('view') && permissions.has('mes-process-cost-rate:query'))
const canDraft = computed(() => allowed('rate-draft') && permissions.has('mes-process-cost-rate:edit'))
const rows = ref<RateMaintenance | null>(null)
const search = ref('')
const date = ref(finance.businessDate)
const loading = ref(false)
const message = ref('')
const messageType = ref<'error' | 'success' | 'warning'>('error')
const requiresReload = ref(false)
const showId = ref('')
const draftOpen = ref(false)
const draftError = ref('')
const release = ref<RatePreview | null>(null)
const releaseError = ref('')
const blankDraft = () => ({ basisId: '', centerId: '', laborRate: '', overheadRate: '', toExclusive: '' })
const draft = reactive(blankDraft())
const selectedChoice = computed(() => rows.value?.choices.find(choice => choice.id === draft.basisId))
const availableCenters = computed(() => rows.value?.centers.filter(center => center.operationId == null
  || center.operationId === selectedChoice.value?.operationId) || [])
const selectedCenter = computed(() => availableCenters.value.find(center => center.locator.versionId === draft.centerId))
const numeric = /^(?:0|[1-9][0-9]{0,19})(?:\.[0-9]{1,8})?$/
const draftValid = computed(() => !!selectedChoice.value && !!selectedCenter.value && numeric.test(draft.laborRate)
  && numeric.test(draft.overheadRate) && (!draft.toExclusive || draft.toExclusive > (rows.value?.businessDate || '')))
const timeline = computed(() => [
  { title: '发布前', items: release.value?.before || [] }, { title: '发布后', items: release.value?.after || [] },
])
const stateLabel = (state: RateState) => ({ DRAFT: '草稿', SUBMITTED: '待复核', APPROVED: '待发布', RELEASED: '已发布' })[state]
const interval = ({ from, toExclusive }: Pick<RateSegment, 'from' | 'toExclusive'>) => from + ' 至 ' + (toExclusive || '长期')
const intent = useOperationsIntent(clear)
const busy = intent.busy
let readSequence = 0

function clear() {
  readSequence++
  rows.value = null
  draftOpen.value = false
  Object.assign(draft, blankDraft())
  release.value = null
  showId.value = ''
  loading.value = false
  message.value = ''
  draftError.value = ''
  releaseError.value = ''
  requiresReload.value = false
}

async function reload() {
  if (!canRead.value || !date.value) return
  const run = intent.capture()
  const sequence = ++readSequence
  rows.value = null
  loading.value = true
  try {
    const result = await manufacturingCostApi.rates(finance.token, search.value, date.value)
    if (!intent.current(run) || !canRead.value || sequence !== readSequence) return
    rows.value = result
    requiresReload.value = false
  } catch (failure) {
    if (intent.current(run) && sequence === readSequence) {
      clear()
      messageType.value = 'error'
      message.value = maintenanceMessage(failure, '无法读取当前费率版本。')
    }
  } finally {
    if (intent.current(run) && sequence === readSequence) loading.value = false
  }
}

function openDraft() {
  if (!canDraft.value || loading.value || requiresReload.value) return
  Object.assign(draft, blankDraft())
  draftError.value = ''
  draftOpen.value = true
}

async function saveDraft() {
  const choice = selectedChoice.value, center = selectedCenter.value
  if (!canDraft.value || !draftValid.value || !choice || !center || !rows.value || requiresReload.value) return
  const payload = {
    workCenterId: choice.workCenterId, operationProfileId: choice.operationId, costCenter: center.locator,
    policyVersionId: choice.policyVersionId, basisId: choice.id, laborRate: draft.laborRate,
    overheadRate: draft.overheadRate, from: rows.value.businessDate, toExclusive: draft.toExclusive || null,
  }
  await change(() => manufacturingCostApi.draftRate(finance.token, payload), 'DRAFT', '已保存费率草稿。', true)
}

async function transition(row: RateVersion, action: 'submit' | 'review') {
  if (loading.value || requiresReload.value) return
  if (action === 'submit' ? !canDraft.value || !row.madeByCurrentUser || row.state !== 'DRAFT'
    : !allowed('rate-review') || row.madeByCurrentUser || row.state !== 'SUBMITTED') return
  await change(() => manufacturingCostApi.transitionRate(finance.token, row.rateId, action, row.revision),
    action === 'submit' ? 'SUBMITTED' : 'APPROVED', action === 'submit' ? '已送审。' : '已批准。')
}

async function change(write: () => Promise<{ state: RateState }>, expected: RateState, success: string, isDraft = false) {
  const run = intent.begin()
  if (!run) return
  message.value = ''
  draftError.value = ''
  try {
    const response = await write()
    if (!intent.current(run) || !canRead.value) return
    if (response.state !== expected) throw new Error('Unconfirmed response')
    draftOpen.value = false
    messageType.value = 'success'
    message.value = success
    await reload()
  } catch (failure) {
    if (!intent.current(run)) return
    const status = (failure as { response?: { status?: number } })?.response?.status
    if (!status || status >= 500 || status === 408 || status === 429) {
      draftOpen.value = false
      requiresReload.value = true
      messageType.value = 'warning'
      message.value = '结果待确认，请查询费率版本。'
    } else if (isDraft) draftError.value = maintenanceMessage(failure)
    else {
      messageType.value = 'error'
      message.value = maintenanceMessage(failure)
      await reload()
    }
  } finally { intent.finish(run) }
}

async function preview(row: RateVersion) {
  if (!allowed('rate-release') || row.state !== 'APPROVED' || loading.value || requiresReload.value) return
  const run = intent.begin()
  if (!run) return
  message.value = ''
  release.value = null
  releaseError.value = ''
  try {
    const response = await manufacturingCostApi.previewRate(finance.token, row.rateId, row.revision)
    if (intent.current(run) && canRead.value && allowed('rate-release') && response.rateId === row.rateId) release.value = response
  } catch (failure) {
    if (intent.current(run)) {
      messageType.value = 'error'
      message.value = maintenanceMessage(failure)
    }
  } finally { intent.finish(run) }
}

async function publish() {
  const selection = release.value
  if (!selection || !allowed('rate-release')) return
  const run = intent.begin()
  if (!run) return
  try {
    const response = await manufacturingCostApi.publishRate(finance.token, selection.rateId, selection.token)
    if (!intent.current(run) || !canRead.value || !allowed('rate-release')) return
    if (response.rateId !== selection.rateId || response.state !== 'RELEASED') throw new Error('Unconfirmed release')
    release.value = null
    messageType.value = 'success'
    message.value = '费率版本已发布。'
    await reload()
  } catch (failure) {
    if (!intent.current(run)) return
    const status = (failure as { response?: { status?: number } })?.response?.status
    if (!status || status >= 500 || status === 408 || status === 429)
      releaseError.value = '发布结果待确认。可查看版本状态，或使用同一预览再次提交。'
    else {
      release.value = null
      messageType.value = 'error'
      message.value = maintenanceMessage(failure)
      await reload()
    }
  } finally { intent.finish(run) }
}

watch(canRead, allowedNow => { if (allowedNow) void reload(); else { intent.invalidate(); clear() } })
watch(canDraft, allowedNow => { if (!allowedNow) { draftOpen.value = false; Object.assign(draft, blankDraft()) } })
watch(() => allowed('rate-release'), allowedNow => { if (!allowedNow) release.value = null })
onMounted(() => { if (canRead.value) void reload(); else message.value = '当前账号没有工序费率读取权限。' })
</script>

<style scoped>
.toolbar { margin-top: 12px; }
.toolbar .el-form-item { margin-bottom: 8px; }
.limit-note { font-size: 13px; color: var(--el-text-color-secondary); }
.form-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 8px 18px; }
.form-grid h3 { font-size: 15px; font-weight: 600; }
.timeline { padding-left: 18px; }
.timeline li { margin-bottom: 10px; }
.identifier { overflow-wrap: anywhere; font-size: 12px; }
.el-select { width: 100%; }
</style>
