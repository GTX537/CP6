<template>
  <div class="budget-edit">
    <div class="page-header">
      <h2>{{ t('预算编制') }}</h2>
      <span class="subtitle">{{ t('budget.workbench.subtitle') }}</span>
    </div>

    <el-row :gutter="12">
      <!-- ─── 左面板：方案列表 + 版本列表 ─── -->
      <el-col :span="7">
        <!-- 方案列表 -->
        <el-card shadow="never" class="side-card">
          <template #header>
            <div class="panel-header">
              <span>{{ t('budget.panel.budgets') }}</span>
              <el-button type="primary" size="small" @click="openCreateBudget">{{ t('budget.btn.createBudget') }}</el-button>
            </div>
          </template>
          <div v-loading="budgetsLoading">
            <div
              v-for="b in budgets"
              :key="b.id"
              class="budget-item"
              :class="{ active: selectedBudget?.id === b.id }"
              @click="selectBudget(b)"
            >
              <div class="budget-item-name">{{ b.name }}</div>
              <div class="budget-item-meta">
                <el-tag size="small" type="info">{{ b.fiscalYear }}</el-tag>
                <el-tag v-if="!b.isActive" size="small" type="danger" style="margin-left:4px">{{ t('已停用') }}</el-tag>
              </div>
            </div>
            <div v-if="!budgets.length && !budgetsLoading" class="empty-hint">{{ t('budget.msg.noBudgets') }}</div>
          </div>
        </el-card>

        <!-- 版本列表 -->
        <el-card v-if="selectedBudget" shadow="never" class="side-card" style="margin-top:10px">
          <template #header>
            <div class="panel-header">
              <span>{{ t('budget.panel.versions') }}</span>
              <el-button type="primary" size="small" @click="openCreateVersion">{{ t('budget.btn.createVersion') }}</el-button>
            </div>
          </template>
          <div v-loading="versionsLoading">
            <div
              v-for="v in versions"
              :key="v.id"
              class="version-item"
              :class="{ active: selectedVersion?.id === v.id }"
              @click="selectVersion(v)"
            >
              <div class="version-item-top">
                <span class="version-no">V{{ v.versionNo }}</span>
                <span class="version-name" :title="v.name">{{ v.name }}</span>
                <el-tag v-if="v.isActive" size="small" type="success" style="margin-left:4px">{{ t('budget.field.isActive') }}</el-tag>
              </div>
              <div class="version-item-meta">
                <el-tag :type="BUDGET_VERSION_STATUS_TAG[v.status]" size="small">
                  {{ t(VERSION_STATUS_I18N[v.status] ?? '') }}
                </el-tag>
                <span class="version-actions">
                  <el-button
                    v-if="v.status === 0"
                    link type="warning" size="small"
                    @click.stop="doSubmit(v)">{{ t('budget.btn.submit') }}</el-button>
                  <el-button
                    v-if="v.status === 2"
                    link type="success" size="small"
                    @click.stop="doActivate(v)">{{ t('budget.btn.activate') }}</el-button>
                  <el-button
                    v-if="v.status === 0"
                    link type="danger" size="small"
                    @click.stop="doDeleteVersion(v)">{{ t('删除') }}</el-button>
                </span>
              </div>
            </div>
            <div v-if="!versions.length && !versionsLoading" class="empty-hint">{{ t('budget.msg.noVersions') }}</div>
          </div>
        </el-card>
      </el-col>

      <!-- ─── 右面板：预算行网格 ─── -->
      <el-col :span="17">
        <el-card shadow="never">
          <template #header>
            <div class="panel-header">
              <span>{{ t('budget.panel.lines') }}
                <span v-if="selectedVersion" class="panel-version-tag">
                  — V{{ selectedVersion.versionNo }} {{ selectedVersion.name }}
                </span>
              </span>
              <!-- 工具栏 (仅草稿可操作) -->
              <div v-if="selectedVersion" style="display:flex;gap:6px;align-items:center">
                <template v-if="isDraft">
                  <el-button type="primary" size="small" @click="openAddLine">{{ t('budget.btn.addLine') }}</el-button>
                  <el-upload
                    :show-file-list="false"
                    :before-upload="handleImportPreview"
                    accept=".xlsx,.xls"
                    :auto-upload="true"
                  >
                    <el-button size="small">{{ t('budget.btn.importExcel') }}</el-button>
                  </el-upload>
                  <el-button size="small" @click="openCopyFrom">{{ t('budget.btn.copyVersion') }}</el-button>
                </template>
                <el-button size="small" @click="loadLines">{{ t('刷新') }}</el-button>
              </div>
            </div>
          </template>

          <!-- 非草稿只读提示 -->
          <el-alert
            v-if="selectedVersion && !isDraft"
            :title="t('budget.msg.readOnlyHint')"
            type="info"
            :closable="false"
            show-icon
            style="margin-bottom:10px"
          />

          <el-table
            v-if="selectedVersion"
            :data="lines"
            border stripe size="small"
            max-height="540"
            v-loading="linesLoading"
          >
            <el-table-column :label="t('budget.field.account')" min-width="160" show-overflow-tooltip>
              <template #default="{ row }">{{ accountLabel(row.accountId) }}</template>
            </el-table-column>
            <el-table-column :label="t('budget.field.costCenter')" width="110" show-overflow-tooltip>
              <template #default="{ row }">{{ costCenterLabel(row.costCenterId) }}</template>
            </el-table-column>
            <el-table-column :label="t('budget.field.costObject')" width="120" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.costObjectType">{{ row.costObjectType }}/{{ row.costObjectId }}</span>
                <span v-else class="dim-text">—</span>
              </template>
            </el-table-column>
            <!-- M1~M12 -->
            <el-table-column
              v-for="m in 12"
              :key="m"
              :label="`M${m}`"
              width="72"
              align="right"
            >
              <template #default="{ row }">
                <template v-if="isDraft">
                  <el-input-number
                    v-model="row.periods[m - 1]"
                    :controls="false"
                    size="small"
                    :precision="2"
                    style="width:62px"
                    @change="(val: number | undefined) => onPeriodChange(row, m - 1, val)"
                  />
                </template>
                <template v-else>
                  {{ fmtAmt(row.periods[m - 1]) }}
                </template>
              </template>
            </el-table-column>
            <el-table-column :label="t('budget.field.annualAmount')" width="100" align="right">
              <template #default="{ row }">
                <span class="amount-total">{{ fmtAmt(row.periods.reduce((s: number, x: number) => s + (x || 0), 0)) }}</span>
              </template>
            </el-table-column>
            <el-table-column :label="t('budget.field.controlMode')" width="90" align="center">
              <template #default="{ row }">
                <el-select
                  v-if="isDraft"
                  v-model="row.controlMode"
                  size="small"
                  style="width:80px"
                  @change="(val: number) => onControlModeChange(row, val)"
                >
                  <el-option v-for="(lbl, k) in BUDGET_CONTROL_MODE_LABEL" :key="k" :value="Number(k)" :label="t(CONTROL_MODE_I18N[Number(k)] ?? '')" />
                </el-select>
                <el-tag v-else size="small">{{ t(CONTROL_MODE_I18N[row.controlMode ?? 0] ?? '') }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('budget.field.controlBasis')" width="85" align="center">
              <template #default="{ row }">
                <el-select
                  v-if="isDraft"
                  v-model="row.controlBasis"
                  size="small"
                  style="width:72px"
                  @change="(val: number) => onControlBasisChange(row, val)"
                >
                  <el-option v-for="(lbl, k) in BUDGET_CONTROL_BASIS_LABEL" :key="k" :value="Number(k)" :label="t(CONTROL_BASIS_I18N[Number(k)] ?? '')" />
                </el-select>
                <el-tag v-else size="small">{{ t(CONTROL_BASIS_I18N[row.controlBasis ?? 0] ?? '') }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('操作')" width="68" fixed="right" align="center">
              <template #default="{ row }">
                <el-button
                  v-if="isDraft"
                  link type="danger" size="small"
                  @click="doDeleteLine(row)">{{ t('删除') }}</el-button>
              </template>
            </el-table-column>
          </el-table>

          <div v-if="!selectedVersion" class="empty-hint" style="padding:40px 0;text-align:center">
            {{ t('budget.msg.selectVersion') }}
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- ─── 新建方案对话框 ─── -->
    <el-dialog v-model="createBudgetVisible" :title="t('budget.btn.createBudget')" width="440px">
      <el-form :model="budgetForm" label-width="90px">
        <el-form-item :label="t('budget.field.budgetName')" required>
          <el-input v-model="budgetForm.name" maxlength="100" />
        </el-form-item>
        <el-form-item :label="t('budget.field.fiscalYear')" required>
          <el-input-number v-model="budgetForm.fiscalYear" :min="2000" :max="2099" :controls="false" style="width:100%" />
        </el-form-item>
        <el-form-item :label="t('budget.field.description')">
          <el-input v-model="budgetForm.description" type="textarea" :rows="2" maxlength="200" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createBudgetVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="doCreateBudget">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- ─── 新建版本对话框 ─── -->
    <el-dialog v-model="createVersionVisible" :title="t('budget.btn.createVersion')" width="480px">
      <el-form :model="versionForm" label-width="110px">
        <el-form-item :label="t('budget.field.versionName')" required>
          <el-input v-model="versionForm.name" maxlength="100" />
        </el-form-item>
        <el-form-item :label="t('budget.field.copyFrom')">
          <el-select v-model="versionForm.copyMode" style="width:100%" @change="onCopyModeChange">
            <el-option value="none" :label="t('budget.field.copyFromNone')" />
            <el-option
              v-for="v in versions"
              :key="v.id"
              :value="`ver:${v.id}`"
              :label="`V${v.versionNo} ${v.name}`"
            />
            <el-option value="actual" :label="t('budget.field.copyFromYear')" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="versionForm.copyMode === 'actual'" :label="t('budget.field.fiscalYear')">
          <el-input-number v-model="versionForm.copyActualYear" :min="2000" :max="2099" :controls="false" style="width:100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVersionVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="doCreateVersion">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- ─── 新增预算行对话框 ─── -->
    <el-dialog v-model="addLineVisible" :title="t('budget.btn.addLine')" width="560px">
      <el-form :model="lineForm" label-width="110px">
        <el-form-item :label="t('budget.field.account')" required>
          <el-select
            v-model="lineForm.accountId"
            filterable
            style="width:100%"
            :placeholder="t('budget.msg.selectAccount')"
            @visible-change="(v: boolean) => v && loadLeafAccounts()"
          >
            <el-option
              v-for="a in leafAccounts"
              :key="a.id"
              :value="a.id"
              :label="`${a.code} ${a.name}`"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('budget.field.costCenter')">
          <el-select
            v-model="lineForm.costCenterId"
            filterable
            clearable
            style="width:100%"
            :placeholder="t('budget.msg.optionalCompanyLevel')"
          >
            <el-option
              v-for="cc in costCenters"
              :key="cc.id"
              :value="cc.id"
              :label="`${cc.code} ${cc.name}`"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('budget.field.costObject')">
          <el-row :gutter="6">
            <el-col :span="10">
              <el-input v-model="lineForm.costObjectType" :placeholder="t('budget.msg.costObjectTypePlaceholder')" maxlength="50" />
            </el-col>
            <el-col :span="14">
              <el-input v-model="lineForm.costObjectId" :placeholder="t('budget.msg.costObjectIdPlaceholder')" maxlength="100" />
            </el-col>
          </el-row>
        </el-form-item>
        <el-form-item :label="t('budget.field.annualAmount')" required>
          <el-input-number v-model="lineForm.annualAmount" :precision="2" :controls="false" style="width:100%" />
        </el-form-item>
        <el-form-item :label="t('budget.field.spreadMode')">
          <el-select v-model="lineForm.spreadMode" style="width:100%">
            <el-option value="even" :label="t('budget.spreadMode.even')" />
            <el-option value="seasonal" :label="t('budget.spreadMode.seasonal')" />
            <el-option value="manual" :label="t('budget.spreadMode.manual')" />
          </el-select>
        </el-form-item>
        <!-- 手工/季节模式显示12期输入 -->
        <el-form-item v-if="lineForm.spreadMode !== 'even'" :label="t('budget.msg.monthlyBreakdown')">
          <el-row :gutter="4">
            <el-col v-for="m in 12" :key="m" :span="4" style="margin-bottom:4px">
              <el-input-number
                v-model="lineForm.periods[m - 1]"
                :controls="false"
                size="small"
                :precision="2"
                :placeholder="`M${m}`"
                style="width:100%"
              />
            </el-col>
          </el-row>
        </el-form-item>
        <el-form-item :label="t('budget.field.controlMode')">
          <el-select v-model="lineForm.controlMode" style="width:100%">
            <el-option v-for="(lbl, k) in BUDGET_CONTROL_MODE_LABEL" :key="k" :value="Number(k)" :label="t(CONTROL_MODE_I18N[Number(k)] ?? '')" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('budget.field.controlBasis')">
          <el-select v-model="lineForm.controlBasis" style="width:100%">
            <el-option v-for="(lbl, k) in BUDGET_CONTROL_BASIS_LABEL" :key="k" :value="Number(k)" :label="t(CONTROL_BASIS_I18N[Number(k)] ?? '')" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('budget.field.memo')">
          <el-input v-model="lineForm.memo" maxlength="200" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="addLineVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="doAddLine">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- ─── Excel 导入预览对话框 ─── -->
    <el-dialog v-model="importPreviewVisible" :title="t('budget.btn.previewImport')" width="720px">
      <el-alert
        v-if="importPreviewResult?.hasFatal"
        :title="t('budget.msg.importHasFatal')"
        type="error"
        :closable="false"
        show-icon
        style="margin-bottom:10px"
      />
      <el-table :data="importPreviewResult?.rows || []" border stripe size="small" max-height="360">
        <el-table-column prop="rowNo" label="Row" width="60" align="center" />
        <el-table-column prop="accountCode" :label="t('budget.field.account')" width="120" />
        <el-table-column prop="costCenterCode" :label="t('budget.field.costCenter')" width="100" show-overflow-tooltip />
        <el-table-column v-for="m in 12" :key="m" :label="`M${m}`" width="72" align="right">
          <template #default="{ row }">{{ fmtAmt(row.periods?.[m - 1]) }}</template>
        </el-table-column>
        <el-table-column :label="t('budget.field.status')" width="80" align="center" fixed="right">
          <template #default="{ row }">
            <el-tag :type="row.ok ? 'success' : 'danger'" size="small">{{ row.ok ? 'OK' : 'ERR' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="error" :label="t('budget.msg.importError')" min-width="140" show-overflow-tooltip />
      </el-table>
      <template #footer>
        <el-button @click="importPreviewVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button
          type="primary"
          :loading="saving"
          :disabled="importPreviewResult?.hasFatal"
          @click="doImportConfirm">{{ t('budget.btn.confirmImport') }}</el-button>
      </template>
    </el-dialog>

    <!-- ─── 复制版本对话框 ─── -->
    <el-dialog v-model="copyFromVisible" :title="t('budget.btn.copyVersion')" width="440px">
      <el-form label-width="110px">
        <el-form-item :label="t('budget.field.copyFrom')">
          <el-select v-model="copyFromVersionId" filterable style="width:100%">
            <el-option
              v-for="v in versions.filter(x => x.id !== selectedVersion?.id)"
              :key="v.id"
              :value="v.id!"
              :label="`V${v.versionNo} ${v.name}`"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="copyFromVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="doCopyFrom">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { budgetApi, budgetVersionApi, budgetLineApi, costCenterApi } from '@/api/fin/budget'
import { glAccountApi } from '@/api/fin/fin'
import type { Budget, BudgetVersion, BudgetLineGridRow, BudgetLineDto, BudgetImportPreviewResult, CostCenter } from '@/types/fin/budget'
import {
  BUDGET_VERSION_STATUS_TAG,
  BUDGET_CONTROL_MODE_LABEL,
  BUDGET_CONTROL_BASIS_LABEL,
} from '@/types/fin/budget'

const { t } = useI18n()

// ── 状态标签 i18n key map ──
const VERSION_STATUS_I18N: Record<number, string> = {
  0: 'budget.status.draft',
  1: 'budget.status.pendingApproval',
  2: 'budget.status.approved',
  3: 'budget.status.rejected',
  4: 'budget.status.archived',
}
const CONTROL_MODE_I18N: Record<number, string> = {
  0: 'budget.controlMode.none',
  1: 'budget.controlMode.warn',
  2: 'budget.controlMode.block',
}
const CONTROL_BASIS_I18N: Record<number, string> = {
  0: 'budget.controlBasis.ytd',
  1: 'budget.controlBasis.period',
}

// ── 数据状态 ──
const budgets = ref<Budget[]>([])
const budgetsLoading = ref(false)
const selectedBudget = ref<Budget | null>(null)

const versions = ref<BudgetVersion[]>([])
const versionsLoading = ref(false)
const selectedVersion = ref<BudgetVersion | null>(null)

const lines = ref<BudgetLineGridRow[]>([])
const linesLoading = ref(false)

const saving = ref(false)

const leafAccounts = ref<any[]>([])
const costCenters = ref<CostCenter[]>([])

// 是否草稿状态（行编辑门控）
const isDraft = computed(() => selectedVersion.value?.status === 0)

// ── 方案相关 ──
const createBudgetVisible = ref(false)
const budgetForm = reactive({ name: '', fiscalYear: new Date().getFullYear(), description: '' })

async function loadBudgets() {
  budgetsLoading.value = true
  try {
    const res = await budgetApi.list()
    budgets.value = res?.data || []
    // 若当前选中方案已不存在则清空
    if (selectedBudget.value && !budgets.value.find(b => b.id === selectedBudget.value!.id)) {
      selectedBudget.value = null
      versions.value = []
      selectedVersion.value = null
      lines.value = []
    }
  } finally {
    budgetsLoading.value = false
  }
}

function openCreateBudget() {
  budgetForm.name = ''
  budgetForm.fiscalYear = new Date().getFullYear()
  budgetForm.description = ''
  createBudgetVisible.value = true
}

async function doCreateBudget() {
  if (!budgetForm.name.trim()) {
    ElMessage.warning(t('budget.field.budgetName') + t('budget.msg.required'))
    return
  }
  saving.value = true
  try {
    const res = await budgetApi.create({ name: budgetForm.name, fiscalYear: budgetForm.fiscalYear, description: budgetForm.description || null })
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.created'))
      createBudgetVisible.value = false
      await loadBudgets()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } finally {
    saving.value = false
  }
}

async function selectBudget(b: Budget) {
  selectedBudget.value = b
  selectedVersion.value = null
  lines.value = []
  await loadVersions()
}

// ── 版本相关 ──
const createVersionVisible = ref(false)
const versionForm = reactive({
  name: '',
  copyMode: 'none',
  copyActualYear: new Date().getFullYear() - 1,
})

async function loadVersions() {
  if (!selectedBudget.value?.id) return
  versionsLoading.value = true
  try {
    const res = await budgetVersionApi.list(selectedBudget.value.id)
    versions.value = res?.data || []
  } finally {
    versionsLoading.value = false
  }
}

function openCreateVersion() {
  versionForm.name = ''
  versionForm.copyMode = 'none'
  versionForm.copyActualYear = (selectedBudget.value?.fiscalYear ?? new Date().getFullYear()) - 1
  createVersionVisible.value = true
}

function onCopyModeChange() {
  // nothing needed — reactive template handles it
}

async function doCreateVersion() {
  if (!budgetForm.name.trim() && !versionForm.name.trim()) {
    ElMessage.warning(t('budget.field.versionName') + t('budget.msg.required'))
    return
  }
  if (!versionForm.name.trim()) {
    ElMessage.warning(t('budget.field.versionName') + t('budget.msg.required'))
    return
  }
  if (!selectedBudget.value?.id) return
  saving.value = true
  try {
    const mode = versionForm.copyMode
    const copyFromVersionId = mode.startsWith('ver:') ? mode.slice(4) : null
    const copyFromActualFiscalYear = mode === 'actual' ? versionForm.copyActualYear : null
    const res = await budgetVersionApi.create(selectedBudget.value.id, {
      name: versionForm.name,
      copyFromVersionId,
      copyFromActualFiscalYear,
    })
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.versionCreated'))
      createVersionVisible.value = false
      await loadVersions()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } finally {
    saving.value = false
  }
}

async function selectVersion(v: BudgetVersion) {
  selectedVersion.value = v
  await loadLines()
}

async function doSubmit(v: BudgetVersion) {
  try {
    await ElMessageBox.confirm(t('budget.msg.submitConfirm'), t('budget.btn.submit'), { type: 'warning' })
  } catch { return }
  try {
    const res = await budgetVersionApi.submit(v.id!)
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.submitted'))
      await loadVersions()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('操作失败'))
  }
}

async function doActivate(v: BudgetVersion) {
  try {
    await ElMessageBox.confirm(t('budget.msg.activateConfirm'), t('budget.btn.activate'), { type: 'warning' })
  } catch { return }
  try {
    const res = await budgetVersionApi.activate(v.id!)
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.activated'))
      await loadVersions()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('操作失败'))
  }
}

async function doDeleteVersion(v: BudgetVersion) {
  try {
    await ElMessageBox.confirm(t('budget.msg.deleteVersionConfirm'), t('budget.btn.deleteVersion'), { type: 'warning' })
  } catch { return }
  try {
    const res = await budgetVersionApi.remove(v.id!)
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.deleted'))
      if (selectedVersion.value?.id === v.id) {
        selectedVersion.value = null
        lines.value = []
      }
      await loadVersions()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('操作失败'))
  }
}

// ── 预算行相关 ──
const addLineVisible = ref(false)

function emptyLineForm() {
  return {
    accountId: '',
    costCenterId: null as string | null,
    costObjectType: '',
    costObjectId: '',
    annualAmount: 0,
    spreadMode: 'even' as 'even' | 'seasonal' | 'manual',
    periods: Array(12).fill(0) as number[],
    controlMode: 0 as number,
    controlBasis: 0 as number,
    memo: '',
    rowVersion: null as string | null,
  }
}

const lineForm = reactive(emptyLineForm())

async function loadLines() {
  if (!selectedVersion.value?.id) return
  linesLoading.value = true
  try {
    const res = await budgetLineApi.list(selectedVersion.value.id)
    lines.value = (res?.data || []).map(row => ({
      ...row,
      periods: row.periods?.length === 12 ? [...row.periods] : Array(12).fill(0),
      controlMode: row.controlMode ?? 0,
      controlBasis: row.controlBasis ?? 0,
    }))
  } finally {
    linesLoading.value = false
  }
}

function openAddLine() {
  Object.assign(lineForm, emptyLineForm())
  addLineVisible.value = true
}

async function doAddLine() {
  if (!lineForm.accountId) {
    ElMessage.warning(t('budget.msg.accountRequired'))
    return
  }
  if (!selectedVersion.value?.id) return
  saving.value = true
  try {
    const dto: BudgetLineDto = {
      versionId: selectedVersion.value.id,
      accountId: lineForm.accountId,
      costCenterId: lineForm.costCenterId || null,
      costObjectType: lineForm.costObjectType || null,
      costObjectId: lineForm.costObjectId || null,
      annualAmount: lineForm.annualAmount,
      spreadMode: lineForm.spreadMode,
      periods: lineForm.spreadMode !== 'even' ? [...lineForm.periods] : null,
      controlMode: lineForm.controlMode as import('@/types/fin/budget').BudgetControlMode,
      controlBasis: lineForm.controlBasis as import('@/types/fin/budget').BudgetControlBasis,
      memo: lineForm.memo || null,
      rowVersion: null,
    }
    const res = await budgetLineApi.upsert(dto)
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.saved'))
      addLineVisible.value = false
      await loadLines()
    } else if (res?.message === 'E-A5-CONCURRENCY-001') {
      await ElMessageBox.alert(t('E-A5-CONCURRENCY-001'), t('budget.msg.concurrencyTitle'), { type: 'warning' })
      await loadLines()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } catch (e: any) {
    const code = e?.response?.data?.message ?? e?.message
    if (code === 'E-A5-CONCURRENCY-001') {
      await ElMessageBox.alert(t('E-A5-CONCURRENCY-001'), t('budget.msg.concurrencyTitle'), { type: 'warning' })
      await loadLines()
    } else {
      ElMessage.error(code || t('操作失败'))
    }
  } finally {
    saving.value = false
  }
}

// 行内 period 编辑后立即 upsert
async function onPeriodChange(row: BudgetLineGridRow, periodIdx: number, _val: number | undefined) {
  if (!isDraft.value || !selectedVersion.value?.id) return
  await upsertRow(row)
}

// 控制模式改变后 upsert
async function onControlModeChange(row: BudgetLineGridRow, val: number) {
  if (!isDraft.value || !selectedVersion.value?.id) return
  row.controlMode = val as import('@/types/fin/budget').BudgetControlMode
  await upsertRow(row)
}

// 控制口径改变后 upsert
async function onControlBasisChange(row: BudgetLineGridRow, val: number) {
  if (!isDraft.value || !selectedVersion.value?.id) return
  row.controlBasis = val as import('@/types/fin/budget').BudgetControlBasis
  await upsertRow(row)
}

async function upsertRow(row: BudgetLineGridRow) {
  if (!selectedVersion.value?.id) return
  const dto: BudgetLineDto = {
    id: row.id,
    versionId: selectedVersion.value.id,
    accountId: row.accountId,
    costCenterId: row.costCenterId || null,
    costObjectType: row.costObjectType || null,
    costObjectId: row.costObjectId || null,
    annualAmount: row.periods.reduce((s, x) => s + (x || 0), 0),
    spreadMode: 'manual',
    periods: [...row.periods],
    controlMode: (row.controlMode ?? 0) as import('@/types/fin/budget').BudgetControlMode,
    controlBasis: (row.controlBasis ?? 0) as import('@/types/fin/budget').BudgetControlBasis,
    memo: row.memo || null,
    rowVersion: row.rowVersion ?? null,
  }
  try {
    const res = await budgetLineApi.upsert(dto)
    if (res?.code === 0) {
      // refresh rowVersion from reload (do a silent reload)
      const refreshed = await budgetLineApi.list(selectedVersion.value!.id)
      const updated = (refreshed?.data || []).find(r => r.id === row.id)
      if (updated) row.rowVersion = updated.rowVersion ?? null
    } else if (res?.message === 'E-A5-CONCURRENCY-001') {
      await ElMessageBox.alert(t('E-A5-CONCURRENCY-001'), t('budget.msg.concurrencyTitle'), { type: 'warning' })
      await loadLines()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } catch (e: any) {
    const code = e?.response?.data?.message ?? e?.message
    if (code === 'E-A5-CONCURRENCY-001') {
      await ElMessageBox.alert(t('E-A5-CONCURRENCY-001'), t('budget.msg.concurrencyTitle'), { type: 'warning' })
      await loadLines()
    } else {
      ElMessage.error(code || t('操作失败'))
    }
  }
}

async function doDeleteLine(row: BudgetLineGridRow) {
  try {
    await ElMessageBox.confirm(t('budget.msg.deleteLineConfirm'), t('budget.btn.deleteLine'), { type: 'warning' })
  } catch { return }
  try {
    const res = await budgetLineApi.remove(row.id)
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.deleted'))
      await loadLines()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('操作失败'))
  }
}

// ── Excel 导入 ──
const importPreviewVisible = ref(false)
const importPreviewResult = ref<BudgetImportPreviewResult | null>(null)
let pendingImportFile: File | null = null

async function handleImportPreview(file: File): Promise<boolean> {
  if (!selectedVersion.value?.id) return false
  pendingImportFile = file
  try {
    const res = await budgetLineApi.importPreview(selectedVersion.value.id, file)
    if (res?.code === 0) {
      importPreviewResult.value = res.data
      importPreviewVisible.value = true
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || t('操作失败'))
  }
  return false // prevent auto-upload
}

async function doImportConfirm() {
  if (!selectedVersion.value?.id || !pendingImportFile) return
  saving.value = true
  try {
    const res = await budgetLineApi.importConfirm(selectedVersion.value.id, pendingImportFile)
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.importDone'))
      importPreviewVisible.value = false
      pendingImportFile = null
      await loadLines()
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } finally {
    saving.value = false
  }
}

// ── 复制版本对话框 ──
const copyFromVisible = ref(false)
const copyFromVersionId = ref('')

function openCopyFrom() {
  copyFromVersionId.value = ''
  copyFromVisible.value = true
}

async function doCopyFrom() {
  if (!copyFromVersionId.value || !selectedBudget.value?.id) {
    ElMessage.warning(t('budget.field.copyFrom') + t('budget.msg.required'))
    return
  }
  saving.value = true
  try {
    // Create a new draft version that copies from the selected version
    const autoName = `${t('budget.field.copyFrom')} V${versions.value.find(v => v.id === copyFromVersionId.value)?.versionNo ?? '?'}`
    const res = await budgetVersionApi.create(selectedBudget.value.id, {
      name: autoName,
      copyFromVersionId: copyFromVersionId.value,
      copyFromActualFiscalYear: null,
    })
    if (res?.code === 0) {
      ElMessage.success(t('budget.msg.versionCreated'))
      copyFromVisible.value = false
      await loadVersions()
      // auto-select the new version
      if (res.data?.id) {
        const newVer = versions.value.find(v => v.id === res.data!.id)
        if (newVer) await selectVersion(newVer)
      }
    } else {
      ElMessage.error(res?.message || t('操作失败'))
    }
  } finally {
    saving.value = false
  }
}

// ── 辅助数据 ──
async function loadLeafAccounts() {
  if (leafAccounts.value.length) return
  try {
    const res = await glAccountApi.list()
    leafAccounts.value = (res?.data ?? []).filter((a: any) => a.isLeaf)
  } catch { /* ignore */ }
}

async function loadCostCenters() {
  try {
    const res = await costCenterApi.list()
    costCenters.value = res?.data ?? []
  } catch { /* ignore — dropdown just shows no options */ }
}

function accountLabel(id: string): string {
  const a = leafAccounts.value.find(x => x.id === id)
  return a ? `${a.code} ${a.name}` : id
}

function costCenterLabel(id?: string | null): string {
  if (!id) return '—'
  const cc = costCenters.value.find((x: any) => x.id === id)
  return cc ? `${cc.code} ${cc.name}` : id
}

function fmtAmt(v?: number | null): string {
  if (v == null || v === 0) return '0.00'
  return v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

onMounted(async () => {
  await loadBudgets()
  await loadLeafAccounts()
  loadCostCenters()
})
</script>

<style scoped>
.budget-edit { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.panel-header { display: flex; justify-content: space-between; align-items: center; }
.panel-version-tag { font-size: 12px; color: #909399; font-weight: 400; }
.side-card :deep(.el-card__body) { padding: 8px; }

/* 方案列表项 */
.budget-item {
  padding: 8px 10px;
  border-radius: 4px;
  cursor: pointer;
  margin-bottom: 4px;
  transition: background 0.15s;
}
.budget-item:hover { background: #f5f7fa; }
.budget-item.active { background: #ecf5ff; border-left: 3px solid #409eff; padding-left: 7px; }
.budget-item-name { font-size: 13px; color: #303133; font-weight: 500; }
.budget-item-meta { margin-top: 2px; }

/* 版本列表项 */
.version-item {
  padding: 8px 10px;
  border-radius: 4px;
  cursor: pointer;
  margin-bottom: 4px;
  transition: background 0.15s;
  border: 1px solid transparent;
}
.version-item:hover { background: #f5f7fa; }
.version-item.active { background: #ecf5ff; border-color: #409eff; }
.version-item-top { display: flex; align-items: center; gap: 4px; }
.version-no { font-size: 11px; color: #909399; flex-shrink: 0; }
.version-name { font-size: 12px; color: #303133; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex: 1; }
.version-item-meta { margin-top: 4px; display: flex; align-items: center; justify-content: space-between; }
.version-actions { display: flex; gap: 2px; }

.empty-hint { color: #c0c4cc; font-size: 12px; text-align: center; padding: 12px 0; }
.amount-total { font-weight: 600; color: #303133; }
.dim-text { color: #c0c4cc; }
</style>
