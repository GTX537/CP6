<template>
  <CpPageShell :title="tr('space.publishManagement.title', '版本发布管理')">
    <template #actions>
      <router-link to="/space/location-publish" class="legacy-link">
        {{ tr('space.publishManagement.locationTools', '库位发布工具') }}
      </router-link>
      <el-button :loading="scopeLoading" @click="refreshScope">
        {{ tr('space.common.refresh', '刷新') }}
      </el-button>
    </template>

    <section class="hero-panel">
      <div>
        <div class="eyebrow">SPACE CONTROL PLANE</div>
        <h2>{{ tr('space.publishManagement.hero', '从验证到上线，一条可追溯的发布链') }}</h2>
        <p>{{ tr('space.publishManagement.heroHint', '先确认模型差异与 WMS 影响，再附上外部审批凭据并启动发布。') }}</p>
      </div>
      <div class="scope-controls">
        <label>
          <span>{{ tr('space.publishManagement.site', '站点') }}</span>
          <el-select v-model="siteId" filterable :placeholder="tr('space.publishManagement.selectSite', '选择站点')">
            <el-option v-for="site in sites" :key="site.id" :value="site.id!" :label="`${site.siteCode} · ${site.siteName}`" />
          </el-select>
        </label>
        <label>
          <span>{{ tr('space.publishManagement.candidate', '待发布版本') }}</span>
          <el-select v-model="candidateId" filterable :disabled="!siteId" :placeholder="tr('space.publishManagement.selectCandidate', '选择 Ready 版本')">
            <el-option
              v-for="version in candidates"
              :key="version.id"
              :value="version.id!"
              :label="`v${version.versionNo} · ${version.name}`"
            />
          </el-select>
        </label>
      </div>
      <div v-if="model" class="environment-strip">
        <div><span>{{ tr('space.publishManagement.current', '当前线上') }}</span><strong>{{ publishedVersionLabel }}</strong></div>
        <div><span>{{ tr('space.publishManagement.mode', '模型模式') }}</span><strong>{{ model.mode || '—' }}</strong></div>
        <div><span>{{ tr('space.publishManagement.cutover', '切换状态') }}</span><strong>{{ model.cutoverState || '—' }}</strong></div>
      </div>
    </section>

    <CpEmpty v-if="!siteId" :text="tr('space.publishManagement.emptySite', '先选择站点，再开始发布检查。')" />

    <template v-else>
      <section class="stage-panel" aria-live="polite">
        <el-steps :active="activeStage" finish-status="success" align-center>
          <el-step :title="tr('space.publishManagement.stage.validate', '验证')" :description="validationStageText" />
          <el-step :title="tr('space.publishManagement.stage.preview', '差异预览')" :description="previewStageText" />
          <el-step :title="tr('space.publishManagement.stage.approval', '审批凭据')" :description="approvalStageText" />
          <el-step :title="tr('space.publishManagement.stage.progress', '发布进度')" :description="attemptStageText" />
        </el-steps>
      </section>

      <div class="work-grid">
        <main class="work-main">
          <section class="work-card validation-card">
            <header class="card-head">
              <div>
                <span class="step-kicker">01 / VALIDATE</span>
                <h3>{{ tr('space.publishManagement.validationTitle', '发布前验证') }}</h3>
              </div>
              <el-button
                v-permission="'space:model:validate'"
                type="primary"
                :disabled="!candidateId"
                :loading="validationLoading"
                @click="startValidation"
              >
                {{ validation ? tr('space.publishManagement.revalidate', '重新验证') : tr('space.publishManagement.validate', '开始验证') }}
              </el-button>
            </header>
            <CpEmpty v-if="!candidateId" :text="tr('space.publishManagement.pickCandidate', '请选择一个 Ready 版本。')" />
            <div v-else-if="validation" class="validation-body">
              <div class="metric-row">
                <div class="metric"><span>{{ tr('space.publishManagement.status', '状态') }}</span><CpTag :tone="statusTone(validation.status)">{{ statusLabel(validation.status) }}</CpTag></div>
                <div class="metric"><span>{{ tr('space.publishManagement.blocking', '阻断') }}</span><strong class="danger-num">{{ validation.blockingCount || 0 }}</strong></div>
                <div class="metric"><span>{{ tr('space.publishManagement.warning', '警告') }}</span><strong>{{ validation.warningCount || 0 }}</strong></div>
                <div class="metric"><span>{{ tr('space.publishManagement.info', '提示') }}</span><strong>{{ validation.infoCount || 0 }}</strong></div>
              </div>
              <el-alert v-if="validation.failureSummary" type="error" :closable="false" show-icon :title="validation.failureCode || tr('space.publishManagement.validationFailed', '验证失败')" :description="validation.failureSummary" />
              <el-table v-if="validation.issues?.length" :data="validation.issues" size="small" max-height="280">
                <el-table-column prop="severity" :label="tr('space.publishManagement.severity', '级别')" width="90" />
                <el-table-column prop="code" :label="tr('space.publishManagement.code', '代码')" width="180" />
                <el-table-column prop="messageArgsJson" :label="tr('space.publishManagement.evidence', '问题证据')" min-width="260" show-overflow-tooltip />
                <el-table-column prop="suggestedActionCode" :label="tr('space.publishManagement.recovery', '建议动作')" min-width="180" />
              </el-table>
            </div>
            <CpEmpty v-else :text="tr('space.publishManagement.notValidated', '尚未验证。验证通过后才能生成发布差异。')" />
          </section>

          <section class="work-card preview-card">
            <header class="card-head">
              <div>
                <span class="step-kicker">02 / PREVIEW</span>
                <h3>{{ tr('space.publishManagement.previewTitle', '差异与 WMS 影响') }}</h3>
              </div>
              <el-button :disabled="validation?.status !== 'Passed'" :loading="previewLoading" @click="loadPreview(false)">
                {{ tr('space.publishManagement.refreshPreview', '生成差异') }}
              </el-button>
            </header>
            <template v-if="preview">
              <div class="preview-summary">
                <div><span>{{ tr('space.publishManagement.changes', '变更') }}</span><strong>{{ preview.changeCount || 0 }}</strong></div>
                <div><span>{{ tr('space.publishManagement.wmsWrites', 'WMS 写入') }}</span><strong>{{ wmsWriteCount }}</strong></div>
                <div><span>{{ tr('space.publishManagement.runtimeOnly', '仅运行时') }}</span><strong>{{ preview.wmsImpact?.runtimeOnlyCount || 0 }}</strong></div>
                <div :class="{ blocked: (preview.wmsImpact?.blockingCount || 0) > 0 }"><span>{{ tr('space.publishManagement.blockers', '阻断影响') }}</span><strong>{{ preview.wmsImpact?.blockingCount || 0 }}</strong></div>
              </div>
              <div class="filter-bar">
                <el-select v-model="previewFilters.objectType" clearable :placeholder="tr('space.publishManagement.objectType', '对象类型')">
                  <el-option v-for="item in objectTypes" :key="item" :label="item" :value="item" />
                </el-select>
                <el-select v-model="previewFilters.action" clearable :placeholder="tr('space.publishManagement.action', '动作')">
                  <el-option v-for="item in actions" :key="item" :label="item" :value="item" />
                </el-select>
                <el-select v-model="previewFilters.impactCode" clearable :placeholder="tr('space.publishManagement.impact', '影响')">
                  <el-option v-for="item in impactCodes" :key="item" :label="item" :value="item" />
                </el-select>
                <el-checkbox v-model="previewFilters.includeNoOp">{{ tr('space.publishManagement.includeNoOp', '显示无变化') }}</el-checkbox>
                <el-button @click="loadPreview(false)">{{ tr('space.common.filter', '筛选') }}</el-button>
              </div>
              <el-table :data="previewItems" size="small" max-height="390" stripe>
                <el-table-column prop="sequenceNo" label="#" width="64" />
                <el-table-column prop="objectType" :label="tr('space.publishManagement.objectType', '对象类型')" width="130" />
                <el-table-column :label="tr('space.publishManagement.change', '变更')" min-width="220">
                  <template #default="{ row }">
                    <div class="change-cell"><CpTag :tone="actionTone(row.action)">{{ row.action }}</CpTag><span class="mono">{{ row.beforeCode || '∅' }} → {{ row.afterCode || '∅' }}</span></div>
                  </template>
                </el-table-column>
                <el-table-column prop="impactCode" :label="tr('space.publishManagement.impact', '影响')" min-width="160">
                  <template #default="{ row }"><span :class="{ 'blocking-text': row.blocking }">{{ row.impactCode }}</span></template>
                </el-table-column>
                <el-table-column prop="logicalId" :label="tr('space.publishManagement.logicalId', '逻辑 ID')" min-width="230" show-overflow-tooltip />
              </el-table>
              <div class="preview-foot">
                <span class="hash">Plan {{ shortHash(preview.planHash) }} · Adapter {{ preview.adapterId }}</span>
                <el-button v-if="preview.nextCursor" :loading="previewLoading" @click="loadPreview(true)">{{ tr('space.common.loadMore', '加载更多') }}</el-button>
              </div>
            </template>
            <CpEmpty v-else :text="tr('space.publishManagement.noPreview', '验证通过后生成差异，确认将写入 WMS 和运行时的内容。')" />
          </section>

          <section class="work-card approval-card">
            <header class="card-head">
              <div>
                <span class="step-kicker">03 / APPROVAL</span>
                <h3>{{ tr('space.publishManagement.approvalTitle', '外部审批凭据') }}</h3>
              </div>
              <CpTag :tone="approvalConfirmed ? 'ok' : 'warn'">{{ approvalConfirmed ? tr('space.publishManagement.confirmed', '已确认') : tr('space.publishManagement.pending', '待确认') }}</CpTag>
            </header>
            <p class="muted-copy">{{ tr('space.publishManagement.approvalHint', '系统记录审批单号，但不代替你所在组织的审批流程。请在审批完成后继续。') }}</p>
            <el-input v-model="approvalReference" maxlength="500" show-word-limit :placeholder="tr('space.publishManagement.approvalPlaceholder', '审批单、变更单或工单编号（可选）')" />
            <el-alert
              v-if="hasValidationWarnings"
              type="warning"
              :closable="false"
              show-icon
              :title="tr('space.publishManagement.warningConfirmTitle', `发布前必须逐项确认 ${preview?.validationWarningCount || 0} 个 Warning`)"
            />
            <el-checkbox
              v-if="hasValidationWarnings"
              v-model="warningsConfirmed"
              class="warning-check"
              data-test="confirm-publish-warnings"
            >
              {{ tr('space.publishManagement.warningConfirm', '我已逐项复核发布预览中的全部 Warning，并确认接受这些风险。') }}
            </el-checkbox>
            <el-checkbox v-model="approvalConfirmed" class="risk-check">
              {{ tr('space.publishManagement.riskConfirm', '我已核对阻断项、WMS 影响和当前线上版本，并确认可以发布。') }}
            </el-checkbox>
            <el-button
              v-permission="'space:model:publish'"
              type="primary"
              size="large"
              :loading="attemptLoading"
              :disabled="!canPublish"
              @click="startPublish"
            >
              {{ tr('space.publishManagement.startPublish', '启动生产发布') }}
            </el-button>
          </section>

          <section v-if="attempt" class="work-card progress-card">
            <header class="card-head">
              <div>
                <span class="step-kicker">04 / EXECUTION</span>
                <h3>{{ tr('space.publishManagement.progressTitle', '发布进度') }}</h3>
              </div>
              <CpTag :tone="statusTone(attempt.status)">{{ statusLabel(attempt.status) }}</CpTag>
            </header>
            <div class="progress-overview">
              <div class="progress-orb" :class="statusTone(attempt.status)"><span>{{ attempt.currentStep || 'Requested' }}</span></div>
              <div class="progress-copy">
                <strong>{{ attempt.summary || tr('space.publishManagement.progressing', '发布任务正在处理') }}</strong>
                <span>{{ tr('space.publishManagement.jobAttempts', '任务尝试') }} {{ attempt.jobAttemptCount || 0 }} / {{ attempt.jobMaxAttempts || 0 }}</span>
                <span v-if="attempt.nextAttemptAtUtc">{{ tr('space.publishManagement.nextRetry', '下次自动重试') }} {{ formatDate(attempt.nextAttemptAtUtc) }}</span>
              </div>
              <el-button v-if="canRetryAttempt" v-permission="'space:model:publish'" type="warning" @click="retryVisible = true">
                {{ tr('space.publishManagement.manualRetry', '人工重试') }}
              </el-button>
            </div>
            <el-alert
              v-if="attempt.lastErrorCode || attempt.openReconciliationIssueCount"
              :type="attempt.openReconciliationIssueCount ? 'warning' : 'error'"
              :closable="false"
              show-icon
              :title="attempt.lastErrorCode || tr('space.publishManagement.reconciliation', '需要人工核对')"
              :description="`${attempt.summary || ''}${attempt.openReconciliationIssueCount ? ` · ${attempt.openReconciliationIssueCount} 个未解决核对项` : ''}`"
            />
            <div v-if="attempt.batches?.length" class="batch-grid">
              <div v-for="batch in attempt.batches" :key="batch.id" class="batch-item">
                <span>#{{ batch.batchNo }} · {{ batch.status }}</span>
                <strong>{{ batch.receipts?.length || 0 }} {{ tr('space.publishManagement.receipts', '回执') }}</strong>
              </div>
            </div>
            <el-timeline v-if="attempt.auditEvents?.length" class="audit-line">
              <el-timeline-item v-for="event in attempt.auditEvents" :key="event.id" :timestamp="formatDate(event.occurredAtUtc)" placement="top">
                <strong>{{ event.eventType }} · {{ event.step }}</strong>
                <p>{{ event.summary }}</p>
              </el-timeline-item>
            </el-timeline>
          </section>
        </main>

        <aside class="work-aside">
          <section class="side-card">
            <header><h3>{{ tr('space.publishManagement.activity', '最近发布活动') }}</h3><CpTag tone="info">{{ activities.length }}</CpTag></header>
            <div v-if="activities.length" class="activity-list">
              <button v-for="item in activities" :key="item.id" type="button" class="activity-item" @click="openAttempt(item.id)">
                <span><strong>v{{ item.targetVersionNo }} · {{ item.targetVersionName }}</strong><small>{{ formatDate(item.startedAtUtc) }}</small></span>
                <CpTag :tone="statusTone(item.status)">{{ statusLabel(item.status) }}</CpTag>
              </button>
            </div>
            <CpEmpty v-else :text="tr('space.publishManagement.noActivity', '这个站点还没有发布记录。')" />
          </section>

          <section class="side-card rollback-card">
            <header><h3>{{ tr('space.publishManagement.rollback', '历史版本回退') }}</h3><span class="rollback-mark">↶</span></header>
            <p>{{ tr('space.publishManagement.rollbackHint', '回退会从历史快照创建一个新的生产版本，经过验证后再发布，不会覆盖历史记录。') }}</p>
            <el-button v-permission="'space:model:rollback'" :disabled="!historicalVersions.length || !model?.currentPublishedVersionId" @click="rollbackVisible = true">
              {{ tr('space.publishManagement.openRollback', '选择历史版本') }}
            </el-button>
            <div v-if="republish" class="republish-state" aria-live="polite">
              <CpTag :tone="statusTone(republish.status)">{{ statusLabel(republish.status) }}</CpTag>
              <span>{{ tr('space.publishManagement.newVersion', '新版本') }} v{{ republish.targetVersionNo || '—' }}</span>
            </div>
          </section>
        </aside>
      </div>
    </template>

    <el-dialog v-model="retryVisible" :title="tr('space.publishManagement.retryTitle', '人工重试发布')" width="520px">
      <el-form label-position="top">
        <el-form-item :label="tr('space.publishManagement.retryReason', '重试原因')" required>
          <el-input v-model="retryForm.reason" type="textarea" :rows="3" maxlength="1000" />
        </el-form-item>
        <el-form-item :label="tr('space.publishManagement.resolution', '已采取的处理措施（可选）')">
          <el-input v-model="retryForm.resolution" type="textarea" :rows="3" maxlength="4000" />
        </el-form-item>
      </el-form>
      <template #footer><el-button @click="retryVisible = false">{{ tr('space.common.cancel', '取消') }}</el-button><el-button type="primary" :loading="retryLoading" :disabled="!retryForm.reason.trim()" @click="retryPublish">{{ tr('space.publishManagement.confirmRetry', '确认重试') }}</el-button></template>
    </el-dialog>

    <el-dialog v-model="rollbackVisible" :title="tr('space.publishManagement.rollbackTitle', '启动历史版本回退')" width="580px">
      <el-alert type="warning" :closable="false" show-icon :title="tr('space.publishManagement.rollbackWarning', '这会创建并发布一个新版本；当前线上版本不会被直接修改。')" />
      <el-form label-position="top" class="dialog-form">
        <el-form-item :label="tr('space.publishManagement.historyVersion', '历史版本')" required>
          <el-select v-model="rollbackForm.historicalVersionId" filterable style="width:100%">
            <el-option v-for="version in historicalVersions" :key="version.id" :value="version.id!" :label="`v${version.versionNo} · ${version.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="tr('space.publishManagement.reason', '回退原因')" required><el-input v-model="rollbackForm.reason" type="textarea" :rows="3" maxlength="1000" /></el-form-item>
        <el-form-item :label="tr('space.publishManagement.approvalReference', '审批凭据（可选）')"><el-input v-model="rollbackForm.approvalReference" maxlength="500" /></el-form-item>
        <el-form-item :label="tr('space.publishManagement.newVersionName', '新版本名称（可选）')"><el-input v-model="rollbackForm.newVersionName" maxlength="200" /></el-form-item>
        <el-checkbox v-model="rollbackForm.confirmed">{{ tr('space.publishManagement.rollbackConfirm', '我确认以上历史版本将基于当前线上版本执行安全回退。') }}</el-checkbox>
      </el-form>
      <template #footer><el-button @click="rollbackVisible = false">{{ tr('space.common.cancel', '取消') }}</el-button><el-button type="danger" :loading="rollbackLoading" :disabled="!canRollback" @click="startRollback">{{ tr('space.publishManagement.confirmRollback', '启动回退') }}</el-button></template>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { useTOr } from '@/i18n/tOr'
import { siteApi } from '@/api/space/site'
import { publishManagementApi, type SpacePublishAttemptSummary } from '@/api/space/publishManagement'
import type { SiteVO } from '@/types/space/scene'
import type {
  ISpaceHistoricalRepublishDto,
  ISpaceModelDto,
  ISpacePublishAttemptDto,
  ISpacePublishPreviewDto,
  ISpacePublishPreviewItemDto,
  ISpaceValidationRunDto,
  ISpaceVersionDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const tr = useTOr()
const route = useRoute()
const requestedSiteId = String(route.query.siteId ?? '')
const requestedVersionId = String(route.query.versionId ?? '')
const requestedAction = String(route.query.action ?? '')
const sites = ref<SiteVO[]>([])
const siteId = ref('')
const candidateId = ref('')
const model = ref<ISpaceModelDto>()
const versions = ref<ISpaceVersionDto[]>([])
const activities = ref<SpacePublishAttemptSummary[]>([])
const validation = ref<ISpaceValidationRunDto>()
const preview = ref<ISpacePublishPreviewDto>()
const previewItems = ref<ISpacePublishPreviewItemDto[]>([])
const attempt = ref<ISpacePublishAttemptDto>()
const republish = ref<ISpaceHistoricalRepublishDto>()
const scopeLoading = ref(false)
const validationLoading = ref(false)
const previewLoading = ref(false)
const attemptLoading = ref(false)
const retryLoading = ref(false)
const rollbackLoading = ref(false)
const approvalReference = ref('')
const approvalConfirmed = ref(false)
const warningsConfirmed = ref(false)
const retryVisible = ref(false)
const rollbackVisible = ref(false)
const retryForm = reactive({ reason: '', resolution: '' })
const rollbackForm = reactive({ historicalVersionId: '', reason: '', approvalReference: '', newVersionName: '', confirmed: false })
const previewFilters = reactive({ objectType: '', action: '', impactCode: '', includeNoOp: false })
const objectTypes = ['Floor', 'Zone', 'Aisle', 'Rack', 'Location']
const actions = ['Create', 'UpdateMaster', 'UpdateGeometryOnly', 'Disable', 'Restore', 'NoOp']
const impactCodes = ['WmsCreate', 'WmsUpdate', 'WmsDisable', 'WmsRestore', 'WmsNoOp', 'RuntimeOnly', 'Blocking']
let validationTimer: number | undefined
let attemptTimer: number | undefined
let republishTimer: number | undefined
let publishKey = ''
let retryKey = ''
let rollbackKey = ''
let requestedWorkflowHandled = false

const candidates = computed(() => versions.value.filter(
  v => ['Draft', 'Ready'].includes(v.status || '') &&
    (v.purpose || 'Production') === 'Production',
))
const historicalVersions = computed(() => versions.value.filter(v => v.status === 'Superseded' && (v.purpose || 'Production') === 'Production'))
const publishedVersion = computed(() => versions.value.find(v => v.id === model.value?.currentPublishedVersionId))
const publishedVersionLabel = computed(() => publishedVersion.value ? `v${publishedVersion.value.versionNo} · ${publishedVersion.value.name}` : tr('space.publishManagement.none', '尚未发布'))
const wmsWriteCount = computed(() => {
  const impact = preview.value?.wmsImpact
  return (impact?.wmsCreateCount || 0) + (impact?.wmsUpdateCount || 0) + (impact?.wmsDisableCount || 0) + (impact?.wmsRestoreCount || 0)
})
const hasValidationWarnings = computed(() =>
  (preview.value?.validationWarningCount || 0) > 0,
)
const warningAcknowledgementReady = computed(() =>
  !hasValidationWarnings.value || Boolean(
    warningsConfirmed.value && preview.value?.warningAcknowledgementHash,
  ),
)
const canPublish = computed(() => Boolean(
  candidateId.value && validation.value?.status === 'Passed' && preview.value?.publishable &&
  preview.value?.planHash && warningAcknowledgementReady.value &&
  approvalConfirmed.value && !attemptLoading.value,
))
const canRetryAttempt = computed(() => ['FailedNoEffect', 'ManualIntervention', 'ReconciliationRequired'].includes(attempt.value?.status || ''))
const canRollback = computed(() => Boolean(rollbackForm.historicalVersionId && rollbackForm.reason.trim() && rollbackForm.confirmed && model.value?.currentPublishedVersionId))
const activeStage = computed(() => attempt.value ? 4 : approvalConfirmed.value && preview.value ? 3 : preview.value ? 2 : validation.value?.status === 'Passed' ? 1 : 0)
const validationStageText = computed(() => validation.value ? statusLabel(validation.value.status) : tr('space.publishManagement.notStarted', '未开始'))
const previewStageText = computed(() => preview.value ? `${preview.value.changeCount || 0} ${tr('space.publishManagement.items', '项变更')}` : tr('space.publishManagement.pending', '待处理'))
const approvalStageText = computed(() => approvalConfirmed.value ? tr('space.publishManagement.confirmed', '已确认') : tr('space.publishManagement.pending', '待确认'))
const attemptStageText = computed(() => attempt.value ? statusLabel(attempt.value.status) : tr('space.publishManagement.pending', '待处理'))

function newKey(prefix: string) {
  return `${prefix}-${crypto.randomUUID()}`
}

function shortHash(value?: string) {
  return value ? `${value.slice(0, 10)}…${value.slice(-6)}` : '—'
}

function formatDate(value?: Date | string) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString()
}

function statusLabel(status?: string) {
  const labels: Record<string, string> = {
    Queued: '排队中', Running: '执行中', Passed: '已通过', Blocked: '已阻断', Failed: '失败',
    Requested: '已请求', Preflighting: '预检中', ApplyingWms: '写入 WMS', VerifyingWms: '核对 WMS',
    ActivatingRuntime: '激活运行时', Completed: '已完成', FailedNoEffect: '失败（未生效）', WaitingRetry: '等待重试',
    ReconciliationRequired: '需要核对', ManualIntervention: '需要人工处理', SnapshotCloned: '快照已创建',
    ValidationPassed: '验证通过', ValidationBlocked: '验证阻断', PublishQueued: '发布已排队',
  }
  return labels[status || ''] || status || '—'
}

function statusTone(status?: string): Tone {
  if (['Passed', 'Completed', 'ValidationPassed'].includes(status || '')) return 'ok'
  if (['Failed', 'FailedNoEffect', 'Blocked', 'ValidationBlocked'].includes(status || '')) return 'danger'
  if (['WaitingRetry', 'ReconciliationRequired', 'ManualIntervention'].includes(status || '')) return 'warn'
  if (['Queued', 'Running', 'Requested', 'Preflighting', 'ApplyingWms', 'VerifyingWms', 'ActivatingRuntime', 'PublishQueued', 'SnapshotCloned'].includes(status || '')) return 'info'
  return 'muted'
}

function actionTone(action?: string): Tone {
  if (action === 'Create' || action === 'Restore') return 'ok'
  if (action === 'Disable') return 'danger'
  if (action === 'NoOp') return 'muted'
  return 'info'
}

function errorDetail(error: unknown) {
  const response = (error as { response?: { status?: number; data?: Record<string, unknown> } })?.response
  const data = response?.data
  return {
    status: response?.status,
    title: String(data?.title || tr('space.publishManagement.requestFailed', '操作未完成')),
    detail: String(data?.detail || data?.message || tr('space.publishManagement.tryAgain', '请刷新状态后重试。')),
    code: data?.code ? String(data.code) : '',
    recovery: data?.recoveryAction ? String(data.recoveryAction) : '',
  }
}

async function showError(error: unknown) {
  const info = errorDetail(error)
  if (info.status === 409 || info.status === 422) {
    await ElMessageBox.alert(`${info.detail}${info.recovery ? `\n\n建议：${info.recovery}` : ''}`, info.code || info.title, { type: info.status === 409 ? 'warning' : 'error' })
  } else {
    ElMessage.error(info.detail)
  }
}

async function loadSites() {
  try {
    const response = await siteApi.list()
    sites.value = response.data || []
  } catch (error) {
    await showError(error)
  }
}

async function refreshScope() {
  if (!siteId.value) return
  scopeLoading.value = true
  try {
    const [nextModel, versionPage, activityPage] = await Promise.all([
      publishManagementApi.getModel(siteId.value),
      publishManagementApi.getVersions(siteId.value, undefined, 100),
      publishManagementApi.getActivities(siteId.value, undefined, 20),
    ])
    model.value = nextModel
    versions.value = versionPage.items || []
    activities.value = activityPage.items || []
    if (!candidates.value.some(v => v.id === candidateId.value)) {
      candidateId.value = candidates.value.some(v => v.id === requestedVersionId)
        ? requestedVersionId
        : candidates.value[0]?.id || ''
    }
    await nextTick()
    if (
      !requestedWorkflowHandled &&
      candidateId.value === requestedVersionId &&
      ['validate', 'publish'].includes(requestedAction)
    ) {
      requestedWorkflowHandled = true
      await startValidation()
    }
  } catch (error) {
    await showError(error)
  } finally {
    scopeLoading.value = false
  }
}

function clearValidationTimer() { if (validationTimer) window.clearTimeout(validationTimer); validationTimer = undefined }
function clearAttemptTimer() { if (attemptTimer) window.clearTimeout(attemptTimer); attemptTimer = undefined }
function clearRepublishTimer() { if (republishTimer) window.clearTimeout(republishTimer); republishTimer = undefined }

async function startValidation() {
  if (!candidateId.value) return
  clearValidationTimer()
  validationLoading.value = true
  preview.value = undefined
  previewItems.value = []
  approvalConfirmed.value = false
  warningsConfirmed.value = false
  publishKey = ''
  try {
    const response = await publishManagementApi.createValidation(candidateId.value)
    validation.value = response.validation
    if (validation.value?.id) await pollValidation(validation.value.id)
  } catch (error) {
    await showError(error)
  } finally {
    validationLoading.value = false
  }
}

async function pollValidation(id: string) {
  clearValidationTimer()
  try {
    validation.value = await publishManagementApi.getValidation(id)
    if (validation.value.status === 'Passed') {
      await loadPreview(false)
    } else if (['Queued', 'Running'].includes(validation.value.status || '')) {
      validationTimer = window.setTimeout(() => void pollValidation(id), 1800)
    }
  } catch (error) {
    await showError(error)
  }
}

async function loadPreview(append: boolean) {
  if (!candidateId.value || validation.value?.status !== 'Passed') return
  previewLoading.value = true
  try {
    const next = await publishManagementApi.getPreview(candidateId.value, {
      objectType: previewFilters.objectType || undefined,
      action: previewFilters.action || undefined,
      impactCode: previewFilters.impactCode || undefined,
      includeNoOp: previewFilters.includeNoOp,
      limit: 100,
      cursor: append ? preview.value?.nextCursor : undefined,
    })
    previewItems.value = append ? [...previewItems.value, ...(next.items || [])] : (next.items || [])
    if (append && preview.value) preview.value.nextCursor = next.nextCursor
    else preview.value = next
    publishKey = ''
    approvalConfirmed.value = false
    warningsConfirmed.value = false
  } catch (error) {
    await showError(error)
  } finally {
    previewLoading.value = false
  }
}

async function startPublish() {
  if (!canPublish.value || !preview.value?.validationRunId || !preview.value.planHash) return
  attemptLoading.value = true
  if (!publishKey) publishKey = newKey('space-publish')
  try {
    const response = await publishManagementApi.createAttempt(candidateId.value, {
      expectedPublishedVersionId: model.value?.currentPublishedVersionId,
      validationRunId: preview.value.validationRunId,
      planHash: preview.value.planHash,
      approvalReference: approvalReference.value.trim() || undefined,
      warningAcknowledgementHash: hasValidationWarnings.value
        ? preview.value.warningAcknowledgementHash
        : undefined,
    }, publishKey)
    attempt.value = response.attempt
    if (attempt.value?.id) {
      ElMessage.success(tr('space.publishManagement.publishQueued', '发布已进入队列。'))
      await pollAttempt(attempt.value.id)
    }
  } catch (error) {
    await showError(error)
  } finally {
    attemptLoading.value = false
  }
}

async function openAttempt(id: string) {
  clearAttemptTimer()
  try {
    attempt.value = await publishManagementApi.getAttempt(id)
    if (isActiveAttempt(attempt.value.status)) attemptTimer = window.setTimeout(() => void pollAttempt(id), 1800)
  } catch (error) {
    await showError(error)
  }
}

function isActiveAttempt(status?: string) {
  return ['Requested', 'Preflighting', 'ApplyingWms', 'VerifyingWms', 'ActivatingRuntime', 'WaitingRetry'].includes(status || '')
}

async function pollAttempt(id: string) {
  clearAttemptTimer()
  try {
    attempt.value = await publishManagementApi.getAttempt(id)
    if (isActiveAttempt(attempt.value.status)) {
      attemptTimer = window.setTimeout(() => void pollAttempt(id), 1800)
    } else {
      await refreshScope()
    }
  } catch (error) {
    await showError(error)
  }
}

async function retryPublish() {
  if (!attempt.value?.id || !retryForm.reason.trim()) return
  retryLoading.value = true
  if (!retryKey) retryKey = newKey('space-publish-retry')
  try {
    const response = await publishManagementApi.retryAttempt(attempt.value.id, {
      reason: retryForm.reason.trim(), resolution: retryForm.resolution.trim() || undefined,
    }, retryKey)
    attempt.value = response.attempt
    retryVisible.value = false
    retryForm.reason = ''
    retryForm.resolution = ''
    retryKey = ''
    ElMessage.success(tr('space.publishManagement.retryQueued', '重试已进入队列。'))
    if (attempt.value?.id) await pollAttempt(attempt.value.id)
  } catch (error) {
    await showError(error)
  } finally {
    retryLoading.value = false
  }
}

async function startRollback() {
  if (!canRollback.value || !model.value?.currentPublishedVersionId) return
  rollbackLoading.value = true
  if (!rollbackKey) rollbackKey = newKey('space-republish')
  try {
    const response = await publishManagementApi.startRepublish(rollbackForm.historicalVersionId, {
      expectedPublishedVersionId: model.value.currentPublishedVersionId,
      reason: rollbackForm.reason.trim(),
      approvalReference: rollbackForm.approvalReference.trim() || undefined,
      newVersionName: rollbackForm.newVersionName.trim() || undefined,
    }, rollbackKey)
    republish.value = response.republish
    rollbackVisible.value = false
    ElMessage.success(tr('space.publishManagement.rollbackQueued', '安全回退已进入队列。'))
    if (republish.value?.id) await pollRepublish(republish.value.id)
  } catch (error) {
    await showError(error)
  } finally {
    rollbackLoading.value = false
  }
}

async function pollRepublish(id: string) {
  clearRepublishTimer()
  try {
    republish.value = await publishManagementApi.getRepublish(id)
    if (republish.value.publishAttemptId) {
      await openAttempt(republish.value.publishAttemptId)
      await refreshScope()
      return
    }
    if (republish.value.status !== 'ValidationBlocked' && !['Failed', 'Cancelled', 'DeadLetter'].includes(republish.value.jobStatus || '')) {
      republishTimer = window.setTimeout(() => void pollRepublish(id), 1800)
    }
  } catch (error) {
    await showError(error)
  }
}

watch(siteId, async () => {
  clearValidationTimer(); clearAttemptTimer(); clearRepublishTimer()
  candidateId.value = ''; model.value = undefined; versions.value = []; activities.value = []
  validation.value = undefined; preview.value = undefined; previewItems.value = []; attempt.value = undefined; republish.value = undefined
  await refreshScope()
})

watch(candidateId, () => {
  clearValidationTimer()
  validation.value = undefined; preview.value = undefined; previewItems.value = []; attempt.value = undefined
  approvalReference.value = ''; approvalConfirmed.value = false; warningsConfirmed.value = false; publishKey = ''
})

onBeforeUnmount(() => { clearValidationTimer(); clearAttemptTimer(); clearRepublishTimer() })

void loadSites().then(() => {
  if (requestedSiteId && sites.value.some(site => site.id === requestedSiteId)) {
    siteId.value = requestedSiteId
  }
})
</script>

<style scoped>
.legacy-link { color:var(--cp-brand-deep); font-weight:700; text-decoration:none; padding:9px 12px; border-radius:8px; }
.legacy-link:hover { background:var(--cp-brand-bg); }
.hero-panel { position:relative; overflow:hidden; display:grid; grid-template-columns:minmax(260px,1.15fr) minmax(320px,.85fr); gap:24px; padding:28px; border:1px solid var(--cp-line); border-radius:18px; color:#fff; background:linear-gradient(128deg,#11263c 0%,#174d65 60%,#1b6a73 100%); box-shadow:0 16px 40px rgba(17,38,60,.16); }
.hero-panel::after { content:""; position:absolute; right:-80px; top:-110px; width:300px; height:300px; border:1px solid rgba(255,255,255,.16); border-radius:50%; box-shadow:0 0 0 45px rgba(255,255,255,.04),0 0 0 90px rgba(255,255,255,.025); }
.hero-panel h2 { margin:5px 0 8px; font-size:26px; line-height:1.25; }
.hero-panel p { margin:0; max-width:620px; color:rgba(255,255,255,.76); line-height:1.7; }
.eyebrow,.step-kicker { color:#67d9c8; font-size:11px; font-weight:900; letter-spacing:.14em; }
.scope-controls { display:grid; grid-template-columns:1fr 1fr; gap:12px; align-self:center; position:relative; z-index:1; }
.scope-controls label { display:grid; gap:7px; color:rgba(255,255,255,.75); font-size:12px; font-weight:800; }
.scope-controls :deep(.el-select__wrapper) { min-height:44px; }
.environment-strip { grid-column:1/-1; display:flex; gap:34px; padding-top:17px; border-top:1px solid rgba(255,255,255,.15); position:relative; z-index:1; }
.environment-strip div { display:grid; gap:3px; }
.environment-strip span { color:rgba(255,255,255,.6); font-size:11px; }
.environment-strip strong { font-size:13px; }
.stage-panel,.work-card,.side-card { background:var(--cp-surface); border:1px solid var(--cp-line); border-radius:14px; }
.stage-panel { padding:22px 18px 16px; }
.work-grid { display:grid; grid-template-columns:minmax(0,1fr) 330px; gap:16px; align-items:start; }
.work-main,.work-aside { display:flex; flex-direction:column; gap:16px; min-width:0; }
.work-card,.side-card { padding:20px; box-shadow:0 8px 24px rgba(24,44,68,.05); }
.card-head,.side-card header { display:flex; align-items:center; justify-content:space-between; gap:16px; margin-bottom:16px; }
.card-head h3,.side-card h3 { margin:3px 0 0; color:var(--cp-ink); font-size:17px; }
.metric-row,.preview-summary { display:grid; grid-template-columns:repeat(4,1fr); gap:10px; }
.metric,.preview-summary>div { display:grid; gap:5px; padding:14px; border-radius:10px; background:var(--cp-line-soft); }
.metric span,.preview-summary span { color:var(--cp-muted); font-size:11px; font-weight:700; }
.metric strong,.preview-summary strong { color:var(--cp-ink); font-size:22px; }
.danger-num,.preview-summary .blocked strong,.blocking-text { color:var(--cp-danger)!important; }
.validation-body { display:grid; gap:14px; }
.filter-bar { display:grid; grid-template-columns:repeat(3,minmax(130px,1fr)) auto auto; align-items:center; gap:8px; margin:14px 0 10px; }
.change-cell { display:flex; align-items:center; gap:9px; }
.mono,.hash { font-family:ui-monospace,SFMono-Regular,Consolas,monospace; font-size:12px; }
.preview-foot { display:flex; justify-content:space-between; align-items:center; gap:12px; margin-top:12px; color:var(--cp-muted); }
.muted-copy,.rollback-card p { color:var(--cp-muted); line-height:1.7; font-size:13px; }
.risk-check { display:flex; margin:14px 0 18px; white-space:normal; height:auto; }
.progress-overview { display:flex; align-items:center; gap:16px; margin-bottom:16px; }
.progress-orb { display:grid; place-items:center; width:86px; height:86px; flex:0 0 auto; border-radius:50%; background:var(--cp-info-bg); color:var(--cp-info); border:6px solid color-mix(in srgb,currentColor 20%,transparent); text-align:center; font-size:11px; font-weight:900; }
.progress-orb.ok { color:var(--cp-ok); background:var(--cp-ok-bg); }.progress-orb.warn { color:var(--cp-warn); background:var(--cp-warn-bg); }.progress-orb.danger { color:var(--cp-danger); background:var(--cp-danger-bg); }
.progress-copy { display:grid; gap:5px; flex:1; }.progress-copy span { color:var(--cp-muted); font-size:12px; }
.batch-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:8px; margin:14px 0; }
.batch-item { display:flex; justify-content:space-between; padding:10px; background:var(--cp-line-soft); border-radius:8px; font-size:12px; }
.audit-line { margin-top:20px; }.audit-line p { margin:4px 0 0; color:var(--cp-muted); }
.activity-list { display:grid; gap:7px; }
.activity-item { width:100%; min-height:58px; display:flex; align-items:center; justify-content:space-between; gap:8px; padding:10px; border:1px solid transparent; border-radius:9px; background:var(--cp-line-soft); color:var(--cp-ink); text-align:left; cursor:pointer; }
.activity-item:hover { border-color:var(--cp-brand); background:var(--cp-brand-bg); }.activity-item>span { display:grid; gap:4px; min-width:0; }.activity-item strong { white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }.activity-item small { color:var(--cp-muted); }
.rollback-card { background:linear-gradient(160deg,var(--cp-surface),var(--cp-warn-bg)); }.rollback-mark { color:var(--cp-warn); font-size:28px; font-weight:800; }
.republish-state { display:flex; align-items:center; gap:8px; margin-top:12px; color:var(--cp-muted); font-size:12px; }.dialog-form { margin-top:16px; }
@media (max-width:1100px) { .work-grid { grid-template-columns:1fr; }.work-aside { display:grid; grid-template-columns:1fr 1fr; }.hero-panel { grid-template-columns:1fr; } }
@media (max-width:760px) { .hero-panel { padding:20px; }.scope-controls,.metric-row,.preview-summary,.work-aside { grid-template-columns:1fr 1fr; }.filter-bar { grid-template-columns:1fr 1fr; }.environment-strip { flex-wrap:wrap; gap:18px; }.stage-panel { padding:18px 6px 12px; }.stage-panel :deep(.el-step__title) { font-size:11px; line-height:1.3; }.stage-panel :deep(.el-step__description) { display:none; }.progress-overview { align-items:flex-start; flex-wrap:wrap; }.card-head { align-items:flex-start; }.preview-foot { align-items:flex-start; flex-direction:column; } }
@media (max-width:520px) { .scope-controls,.metric-row,.preview-summary,.work-aside,.filter-bar { grid-template-columns:1fr; }.hero-panel h2 { font-size:22px; } }
</style>
