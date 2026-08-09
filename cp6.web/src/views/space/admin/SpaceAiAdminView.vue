<template>
  <CpPageShell :title="tr(copy.title)">
    <template #actions>
      <el-button :loading="loading" @click="loadAll">{{ tr(copy.refresh) }}</el-button>
    </template>

    <el-alert
      class="policy-note"
      type="info"
      :closable="false"
      show-icon
      :title="tr(copy.safetyTitle)"
      :description="tr(copy.safetyDescription)"
    />

    <section class="summary-grid" aria-label="AI usage summary">
      <article class="summary-card">
        <span>{{ tr(copy.totalRuns) }}</span>
        <strong>{{ usage.summary.totalRuns.toLocaleString() }}</strong>
        <small>{{ tr(copy.units, { input: usage.summary.inputUnits, output: usage.summary.outputUnits }) }}</small>
      </article>
      <article class="summary-card">
        <span>{{ tr(copy.dailyBudget) }}</span>
        <strong>{{ budgetRemaining(usage.summary.dailyBudget) }}</strong>
        <small>{{ budgetConsumed(usage.summary.dailyBudget) }}</small>
      </article>
      <article class="summary-card">
        <span>{{ tr(copy.monthlyBudget) }}</span>
        <strong>{{ budgetRemaining(usage.summary.monthlyBudget) }}</strong>
        <small>{{ budgetConsumed(usage.summary.monthlyBudget) }}</small>
      </article>
      <article class="summary-card">
        <span>{{ tr(copy.costStatus) }}</span>
        <strong>{{ usage.summary.hasUnpricedUsage ? tr(copy.includesUnpriced) : tr(copy.priced) }}</strong>
        <small>{{ tr(copy.actualVsEstimated) }}</small>
      </article>
    </section>

    <el-card class="panel" shadow="never">
      <template #header>
        <div class="panel-heading">
          <div>
            <h2>{{ tr(copy.policyTitle) }}</h2>
            <p>{{ tr(copy.policyVersion, { version: form.expectedVersion }) }}</p>
          </div>
          <el-tag :type="form.dataPolicy === 'Disabled' ? 'info' : 'success'">
            {{ policyLabel(form.dataPolicy) }}
          </el-tag>
        </div>
      </template>

      <el-form label-position="top" :model="form" class="policy-form">
        <el-form-item :label="tr(copy.dataPolicy)">
          <el-select v-model="form.dataPolicy">
            <el-option value="Disabled" :label="policyLabel('Disabled')" />
            <el-option value="MetadataOnly" :label="policyLabel('MetadataOnly')" />
            <el-option value="StructuredFeatures" :label="policyLabel('StructuredFeatures')" />
          </el-select>
        </el-form-item>

        <el-form-item :label="tr(copy.allowedSites)">
          <el-select
            v-model="form.allowedSiteIds"
            multiple
            filterable
            collapse-tags
            :placeholder="tr(copy.chooseSites)"
          >
            <el-option
              v-for="site in sites"
              :key="site.id"
              :value="site.id"
              :label="`${site.siteCode} · ${site.siteName}`"
            />
          </el-select>
        </el-form-item>

        <el-form-item :label="tr(copy.providers)">
          <el-select
            v-model="form.allowedProviderAliases"
            multiple
            :placeholder="tr(copy.chooseProviders)"
            @change="syncExternalFlag"
          >
            <el-option
              v-for="provider in policy.approvedProviders"
              :key="provider.alias"
              :value="provider.alias"
              :label="`${provider.alias} · ${provider.kind}`"
            />
          </el-select>
          <span v-if="!policy.approvedProviders.length" class="field-help warning">
            {{ tr(copy.noProviders) }}
          </span>
        </el-form-item>

        <el-form-item :label="tr(copy.concurrency)">
          <el-input-number v-model="form.maxConcurrentRuns" :min="1" :max="3" />
        </el-form-item>

        <el-form-item :label="tr(copy.externalProvider)">
          <el-switch v-model="form.externalProviderEnabled" disabled />
          <span class="field-help">{{ tr(copy.externalDerived) }}</span>
        </el-form-item>

        <el-form-item :label="tr(copy.dailyLimit)">
          <el-input-number v-model="form.dailyBudgetMinor" :min="0" :precision="0" controls-position="right" />
        </el-form-item>

        <el-form-item :label="tr(copy.monthlyLimit)">
          <el-input-number v-model="form.monthlyBudgetMinor" :min="0" :precision="0" controls-position="right" />
        </el-form-item>

        <el-form-item :label="tr(copy.currency)">
          <el-input v-model="form.currency" maxlength="3" placeholder="USD" @input="normalizeCurrency" />
        </el-form-item>
      </el-form>

      <div class="policy-actions">
        <span class="field-help">{{ tr(copy.minorUnits) }}</span>
        <el-button
          v-permission="'space-ai-admin:manage'"
          type="primary"
          :loading="saving"
          :disabled="!canSave"
          @click="savePolicy"
        >
          {{ tr(copy.savePolicy) }}
        </el-button>
      </div>
    </el-card>

    <el-card class="panel" shadow="never">
      <template #header>
        <div class="panel-heading">
          <div>
            <h2>{{ tr(copy.usageTitle) }}</h2>
            <p>{{ tr(copy.usageDescription) }}</p>
          </div>
        </div>
      </template>

      <div class="usage-filters">
        <el-select v-model="filters.providerAlias" clearable :placeholder="tr(copy.allProviders)">
          <el-option
            v-for="provider in policy.approvedProviders"
            :key="provider.alias"
            :value="provider.alias"
            :label="provider.alias"
          />
        </el-select>
        <el-select v-model="filters.outcome" clearable :placeholder="tr(copy.allOutcomes)">
          <el-option value="Succeeded" :label="tr(copy.succeeded)" />
          <el-option value="Failed" :label="tr(copy.failed)" />
          <el-option value="Unknown" :label="tr(copy.unknown)" />
        </el-select>
        <el-button :loading="usageLoading" @click="applyFilters">{{ tr(copy.apply) }}</el-button>
      </div>

      <el-table v-loading="usageLoading" :data="usage.items" :empty-text="tr(copy.noUsage)">
        <el-table-column prop="recordedAtUtc" :label="tr(copy.recordedAt)" min-width="180">
          <template #default="{ row }">{{ formatDate(row.recordedAtUtc) }}</template>
        </el-table-column>
        <el-table-column prop="providerAlias" :label="tr(copy.provider)" min-width="150" />
        <el-table-column prop="providerModel" :label="tr(copy.model)" min-width="160" />
        <el-table-column :label="tr(copy.usageUnits)" min-width="160">
          <template #default="{ row }">{{ row.inputUnits }} / {{ row.outputUnits }}</template>
        </el-table-column>
        <el-table-column :label="tr(copy.cost)" min-width="220">
          <template #default="{ row }">
            <span :class="{ unpriced: !row.currency }">{{ usageCost(row) }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="latencyMs" :label="tr(copy.latency)" min-width="120" />
        <el-table-column prop="outcome" :label="tr(copy.outcome)" min-width="120">
          <template #default="{ row }">
            <el-tag :type="row.outcome === 'Succeeded' ? 'success' : row.outcome === 'Failed' ? 'danger' : 'info'">
              {{ outcomeLabel(row.outcome) }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination
        class="pagination"
        layout="total, prev, pager, next, sizes"
        :total="usage.total"
        :current-page="filters.page"
        :page-size="filters.pageSize"
        :page-sizes="[10, 25, 50, 100]"
        @update:current-page="changePage"
        @update:page-size="changePageSize"
      />
    </el-card>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { siteApi } from '@/api/space/site'
import {
  spaceAiAdminApi,
  type SpaceAiBudgetBalance,
  type SpaceAiDataPolicy,
  type SpaceAiPolicy,
  type SpaceAiUsageItem,
  type SpaceAiUsagePage,
  type UpdateSpaceAiPolicyRequest,
} from '@/api/space/aiAdmin'
import type { SiteVO } from '@/types/space/scene'

const { t } = useI18n()
const copy = {
  title: 'space.aiAdmin.title', refresh: 'space.aiAdmin.refresh',
  safetyTitle: 'space.aiAdmin.safetyTitle', safetyDescription: 'space.aiAdmin.safetyDescription',
  totalRuns: 'space.aiAdmin.totalRuns', units: 'space.aiAdmin.units', dailyBudget: 'space.aiAdmin.dailyBudget',
  monthlyBudget: 'space.aiAdmin.monthlyBudget', costStatus: 'space.aiAdmin.costStatus',
  includesUnpriced: 'space.aiAdmin.includesUnpriced', priced: 'space.aiAdmin.priced',
  actualVsEstimated: 'space.aiAdmin.actualVsEstimated', policyTitle: 'space.aiAdmin.policyTitle',
  policyVersion: 'space.aiAdmin.policyVersion', dataPolicy: 'space.aiAdmin.dataPolicy',
  allowedSites: 'space.aiAdmin.allowedSites', chooseSites: 'space.aiAdmin.chooseSites',
  providers: 'space.aiAdmin.providers', chooseProviders: 'space.aiAdmin.chooseProviders',
  noProviders: 'space.aiAdmin.noProviders', concurrency: 'space.aiAdmin.concurrency',
  externalProvider: 'space.aiAdmin.externalProvider', externalDerived: 'space.aiAdmin.externalDerived',
  dailyLimit: 'space.aiAdmin.dailyLimit', monthlyLimit: 'space.aiAdmin.monthlyLimit',
  currency: 'space.aiAdmin.currency', minorUnits: 'space.aiAdmin.minorUnits',
  savePolicy: 'space.aiAdmin.savePolicy', usageTitle: 'space.aiAdmin.usageTitle',
  usageDescription: 'space.aiAdmin.usageDescription', allProviders: 'space.aiAdmin.allProviders',
  allOutcomes: 'space.aiAdmin.allOutcomes', succeeded: 'space.aiAdmin.succeeded',
  failed: 'space.aiAdmin.failed', unknown: 'space.aiAdmin.unknown', apply: 'space.aiAdmin.apply',
  recordedAt: 'space.aiAdmin.recordedAt', provider: 'space.aiAdmin.provider', model: 'space.aiAdmin.model',
  usageUnits: 'space.aiAdmin.usageUnits', cost: 'space.aiAdmin.cost', latency: 'space.aiAdmin.latency',
  outcome: 'space.aiAdmin.outcome', noLimit: 'space.aiAdmin.noLimit', remaining: 'space.aiAdmin.remaining',
  noUsage: 'space.aiAdmin.noUsage',
  consumed: 'space.aiAdmin.consumed', unpriced: 'space.aiAdmin.unpriced', estimated: 'space.aiAdmin.estimated',
  actual: 'space.aiAdmin.actual', saved: 'space.aiAdmin.saved', policyDisabled: 'space.aiAdmin.policyDisabled',
  metadataOnly: 'space.aiAdmin.metadataOnly', structuredFeatures: 'space.aiAdmin.structuredFeatures',
} as const
const tr = (key: string, params?: Record<string, unknown>) => t(key, params || {})

const emptyPolicy = (): SpaceAiPolicy => ({
  version: 0,
  dataPolicy: 'Disabled',
  allowedSiteIds: [],
  allowedProviderAliases: [],
  maxConcurrentRuns: 3,
  externalProviderEnabled: false,
  dailyBudgetMinor: null,
  monthlyBudgetMinor: null,
  currency: null,
  approvedProviders: [],
})
const emptyUsage = (): SpaceAiUsagePage => ({
  items: [], total: 0, page: 1, pageSize: 25,
  summary: {
    totalRuns: 0, inputUnits: 0, outputUnits: 0,
    estimatedCostMinor: 0, actualCostMinor: 0, hasUnpricedUsage: false,
    dailyBudget: { limitMinor: null, consumedMinor: 0, remainingMinor: null, currency: null },
    monthlyBudget: { limitMinor: null, consumedMinor: 0, remainingMinor: null, currency: null },
  },
})

const loading = ref(false)
const saving = ref(false)
const usageLoading = ref(false)
const sites = ref<Array<SiteVO & { id: string }>>([])
const policy = ref<SpaceAiPolicy>(emptyPolicy())
const usage = ref<SpaceAiUsagePage>(emptyUsage())
const form = reactive<UpdateSpaceAiPolicyRequest>({
  expectedVersion: 0, dataPolicy: 'Disabled', allowedSiteIds: [],
  allowedProviderAliases: [], maxConcurrentRuns: 3,
  externalProviderEnabled: false, dailyBudgetMinor: null,
  monthlyBudgetMinor: null, currency: null,
})
const filters = reactive({ providerAlias: '', outcome: '', page: 1, pageSize: 25 })

const canSave = computed(() => {
  if (form.dataPolicy !== 'Disabled' && (!form.allowedSiteIds.length || !form.allowedProviderAliases.length)) return false
  if ((form.dailyBudgetMinor != null || form.monthlyBudgetMinor != null) && !form.currency) return false
  return form.monthlyBudgetMinor == null || form.dailyBudgetMinor == null || form.monthlyBudgetMinor >= form.dailyBudgetMinor
})

function applyPolicy(value: SpaceAiPolicy) {
  policy.value = value
  Object.assign(form, {
    expectedVersion: value.version,
    dataPolicy: value.dataPolicy,
    allowedSiteIds: [...value.allowedSiteIds],
    allowedProviderAliases: [...value.allowedProviderAliases],
    maxConcurrentRuns: value.maxConcurrentRuns,
    externalProviderEnabled: value.externalProviderEnabled,
    dailyBudgetMinor: value.dailyBudgetMinor ?? null,
    monthlyBudgetMinor: value.monthlyBudgetMinor ?? null,
    currency: value.currency ?? null,
  })
}

async function loadPolicyAndSites() {
  const [policyResult, siteResult] = await Promise.all([
    spaceAiAdminApi.getPolicy(),
    siteApi.list(),
  ])
  applyPolicy(policyResult)
  sites.value = (siteResult.data || []).filter((site): site is SiteVO & { id: string } => !!site.id)
}

async function loadUsage() {
  usageLoading.value = true
  try {
    const to = new Date()
    const from = new Date(to.getTime() - 30 * 24 * 60 * 60 * 1000)
    usage.value = await spaceAiAdminApi.getUsage({
      fromUtc: from.toISOString(), toUtc: to.toISOString(),
      providerAlias: filters.providerAlias || undefined,
      outcome: filters.outcome || undefined,
      page: filters.page, pageSize: filters.pageSize,
    })
  } finally {
    usageLoading.value = false
  }
}

async function loadAll() {
  loading.value = true
  try {
    await loadPolicyAndSites()
    await loadUsage()
  } finally {
    loading.value = false
  }
}

function syncExternalFlag() {
  const selected = new Set(form.allowedProviderAliases)
  form.externalProviderEnabled = policy.value.approvedProviders.some(
    provider => provider.kind === 'External' && selected.has(provider.alias),
  )
}

function normalizeCurrency(value: string) {
  form.currency = value ? value.toUpperCase().replace(/[^A-Z]/g, '').slice(0, 3) : null
}

async function savePolicy() {
  saving.value = true
  try {
    const key = globalThis.crypto?.randomUUID?.() || `ai-policy-${Date.now()}`
    const result = await spaceAiAdminApi.updatePolicy({
      ...form,
      allowedSiteIds: [...form.allowedSiteIds],
      allowedProviderAliases: [...form.allowedProviderAliases],
      currency: form.currency || null,
    }, key)
    applyPolicy(result.policy)
    ElMessage.success(tr(copy.saved))
    await loadUsage()
  } catch (error: any) {
    if (error?.response?.status === 409) await loadPolicyAndSites()
  } finally {
    saving.value = false
  }
}

function refreshUsage() { void loadUsage().catch(() => undefined) }
function applyFilters() { filters.page = 1; refreshUsage() }
function changePage(page: number) { filters.page = page; refreshUsage() }
function changePageSize(size: number) { filters.pageSize = size; filters.page = 1; refreshUsage() }
function formatDate(value: string) { return new Date(value).toLocaleString() }
function policyLabel(value: SpaceAiDataPolicy) {
  return value === 'Disabled' ? tr(copy.policyDisabled)
    : value === 'MetadataOnly' ? tr(copy.metadataOnly) : tr(copy.structuredFeatures)
}
function outcomeLabel(value: string) {
  return value === 'Succeeded' ? tr(copy.succeeded) : value === 'Failed' ? tr(copy.failed) : tr(copy.unknown)
}
function budgetRemaining(value: SpaceAiBudgetBalance) {
  if (value.limitMinor == null) return tr(copy.noLimit)
  return `${value.currency || ''} ${value.remainingMinor ?? 0}`.trim()
}
function budgetConsumed(value: SpaceAiBudgetBalance) {
  return tr(copy.consumed, { amount: `${value.currency || ''} ${value.consumedMinor}`.trim() })
}
function usageCost(row: SpaceAiUsageItem) {
  if (!row.currency) return tr(copy.unpriced)
  return row.actualCostMinor == null
    ? tr(copy.estimated, { amount: `${row.currency} ${row.estimatedCostMinor}` })
    : tr(copy.actual, { amount: `${row.currency} ${row.actualCostMinor}` })
}

onMounted(() => { void loadAll().catch(() => undefined) })
</script>

<style scoped>
.policy-note { margin-bottom: 18px; }
.summary-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; margin-bottom: 18px; }
.summary-card { min-height: 116px; padding: 18px; border: 1px solid var(--el-border-color-lighter); border-radius: 12px; background: var(--el-bg-color); display: flex; flex-direction: column; gap: 8px; }
.summary-card span, .summary-card small, .panel-heading p, .field-help { color: var(--el-text-color-secondary); font-size: 13px; }
.summary-card strong { font-size: 24px; line-height: 1.2; color: var(--el-text-color-primary); }
.panel { margin-bottom: 18px; }
.panel-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; }
.panel-heading h2 { margin: 0 0 4px; font-size: 17px; }
.panel-heading p { margin: 0; }
.policy-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); column-gap: 24px; }
.policy-form :deep(.el-select), .policy-form :deep(.el-input-number) { width: 100%; }
.field-help { display: block; margin-top: 6px; }
.field-help.warning, .unpriced { color: var(--el-color-warning); }
.policy-actions { display: flex; justify-content: space-between; align-items: center; gap: 16px; border-top: 1px solid var(--el-border-color-lighter); padding-top: 16px; }
.usage-filters { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 16px; }
.usage-filters .el-select { width: 220px; }
.pagination { justify-content: flex-end; margin-top: 16px; }
@media (max-width: 960px) {
  .summary-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@media (max-width: 640px) {
  .summary-grid, .policy-form { grid-template-columns: 1fr; }
  .policy-actions { align-items: stretch; flex-direction: column; }
  .policy-actions .el-button, .usage-filters .el-select { width: 100%; }
}
</style>
