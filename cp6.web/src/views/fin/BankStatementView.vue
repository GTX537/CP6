<template>
  <div class="bank-statement">
    <div class="page-header">
      <h2>{{ t('bankrecon.statement') }}</h2>
      <span class="subtitle">{{ t('bankrecon.statement.subtitle') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="filterStatus" size="small" style="width:130px" clearable :placeholder="t('全部状态')" @change="load">
          <el-option :value="0" :label="t('bankrecon.status.open')" />
          <el-option :value="1" :label="t('bankrecon.status.locked')" />
        </el-select>
        <el-button type="primary" size="small" @click="openCreate">{{ t('bankrecon.btn.createStatement') }}</el-button>
        <el-button size="small" @click="load">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="520" v-loading="loading" row-key="id">
        <el-table-column prop="no" :label="t('bankrecon.field.statementNo')" width="160" />
        <el-table-column prop="bankAccountId" :label="t('bankrecon.field.bankAccount')" width="130" />
        <el-table-column :label="t('bankrecon.field.period')" width="200">
          <template #default="{ row }">
            {{ (row.periodStart || '').slice(0, 10) }} ~ {{ (row.periodEnd || '').slice(0, 10) }}
          </template>
        </el-table-column>
        <el-table-column prop="openingBalance" :label="t('bankrecon.field.opening')" width="120" align="right" />
        <el-table-column prop="closingBalance" :label="t('bankrecon.field.closing')" width="120" align="right" />
        <el-table-column :label="t('bankrecon.field.status')" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">
              {{ row.status === 1 ? t('bankrecon.status.locked') : t('bankrecon.status.open') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="160" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openImport(row)">{{ t('bankrecon.btn.import') }}</el-button>
            <el-button link type="primary" size="small" @click="goWorkbench(row)">{{ t('bankrecon.workbench') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建会话对话框 -->
    <el-dialog v-model="createVisible" :title="t('bankrecon.btn.createStatement')" width="480px">
      <el-form :model="createForm" label-width="120px" size="small">
        <el-form-item :label="t('bankrecon.field.bankAccount')">
          <el-input v-model="createForm.bankAccountId" :placeholder="t('bankrecon.field.bankAccount')" />
        </el-form-item>
        <el-form-item :label="t('bankrecon.field.period')">
          <el-date-picker v-model="createForm.periodStart" type="date" value-format="YYYY-MM-DD"
            style="width:100%" :placeholder="t('bankrecon.field.periodStart')" />
        </el-form-item>
        <el-form-item :label="t('bankrecon.field.periodEnd')">
          <el-date-picker v-model="createForm.periodEnd" type="date" value-format="YYYY-MM-DD"
            style="width:100%" :placeholder="t('bankrecon.field.periodEnd')" />
        </el-form-item>
        <el-form-item :label="t('bankrecon.field.opening')">
          <el-input-number v-model="createForm.openingBalance" :precision="2" style="width:100%" />
        </el-form-item>
        <el-form-item :label="t('bankrecon.field.closing')">
          <el-input-number v-model="createForm.closingBalance" :precision="2" style="width:100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="doCreate">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>

    <!-- 导入对话框：Preview → Confirm -->
    <el-dialog v-model="importVisible" :title="t('bankrecon.btn.import')" width="640px" @close="resetImport">
      <el-form label-width="100px" size="small">
        <el-form-item :label="t('bankrecon.profile')">
          <el-select v-model="importProfileId" :placeholder="t('bankrecon.profile')" filterable style="width:100%"
            @visible-change="(v: boolean) => v && loadProfiles()">
            <el-option v-for="p in profiles" :key="p.id" :value="p.id!" :label="p.name" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('bankrecon.btn.selectFile')">
          <el-upload :auto-upload="false" :on-change="onFile" :limit="1" :file-list="fileList"
            accept=".csv,.xlsx,.xls" style="width:100%">
            <el-button size="small">{{ t('bankrecon.btn.selectFile') }}</el-button>
          </el-upload>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :disabled="!importFile || !importProfileId" :loading="previewing" @click="doPreview">
            {{ t('bankrecon.btn.preview') }}
          </el-button>
        </el-form-item>
      </el-form>

      <div v-if="preview" class="preview-result">
        <el-alert
          :title="`${t('bankrecon.preview.success')}: ${preview.successCount}  ${t('bankrecon.preview.failed')}: ${preview.failedCount}  ${t('bankrecon.preview.strongDup')}: ${preview.strongDupCount}  ${t('bankrecon.preview.suspectedDup')}: ${preview.suspectedDupCount}`"
          type="info" :closable="false" style="margin-bottom:8px" />
        <el-table v-if="preview.errors.length" :data="preview.errors" border size="small" max-height="200">
          <el-table-column prop="sourceLineNo" :label="t('bankrecon.preview.lineNo')" width="70" />
          <el-table-column prop="code" :label="t('bankrecon.preview.code')" width="160" />
          <el-table-column prop="reason" :label="t('bankrecon.preview.reason')" min-width="200" show-overflow-tooltip />
        </el-table>
      </div>

      <template #footer>
        <el-button @click="importVisible = false">{{ t('取消') }}</el-button>
        <el-button type="primary" :disabled="!preview || preview.failedCount > 0" :loading="confirming" @click="doConfirm">
          {{ t('bankrecon.btn.confirmImport') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { bankStatementApi, bankProfileApi } from '@/api/fin/bankRecon'
import type { BankStatement, BankImportPreviewResult, BankImportProfile } from '@/types/fin/bankRecon'

const { t } = useI18n()
const router = useRouter()

const rows = ref<BankStatement[]>([])
const loading = ref(false)
const saving = ref(false)
const filterStatus = ref<number | undefined>(undefined)

// 新建会话
const createVisible = ref(false)
const createForm = reactive({
  bankAccountId: '',
  fiscalPeriodId: '',
  periodStart: '',
  periodEnd: '',
  openingBalance: 0,
  closingBalance: 0,
})

// 导入
const importVisible = ref(false)
const importTarget = ref<BankStatement | null>(null)
const importProfileId = ref('')
const importFile = ref<File | null>(null)
const fileList = ref<any[]>([])
const preview = ref<BankImportPreviewResult | null>(null)
const profiles = ref<BankImportProfile[]>([])
const previewing = ref(false)
const confirming = ref(false)

async function load() {
  loading.value = true
  try {
    const r = await bankStatementApi.list({ status: filterStatus.value })
    rows.value = r?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(createForm, { bankAccountId: '', fiscalPeriodId: '', periodStart: '', periodEnd: '', openingBalance: 0, closingBalance: 0 })
  createVisible.value = true
}

async function doCreate() {
  if (!createForm.bankAccountId.trim()) { ElMessage.warning(t('bankrecon.field.bankAccount') + t('必填')); return }
  saving.value = true
  try {
    const r = await bankStatementApi.create({ ...createForm })
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.msg.created'))
      createVisible.value = false
      await load()
    } else {
      ElMessage.error(r.message)
    }
  } finally {
    saving.value = false
  }
}

async function loadProfiles() {
  if (profiles.value.length) return
  try {
    const r = await bankProfileApi.list()
    profiles.value = r?.data || []
  } catch { /* ignore */ }
}

function openImport(row: BankStatement) {
  importTarget.value = row
  importVisible.value = true
  preview.value = null
  importFile.value = null
  fileList.value = []
  importProfileId.value = ''
  loadProfiles()
}

function onFile(f: any) {
  importFile.value = f?.raw ?? null
  fileList.value = f ? [f] : []
}

function resetImport() {
  preview.value = null
  importFile.value = null
  fileList.value = []
  importProfileId.value = ''
}

async function doPreview() {
  if (!importTarget.value || !importFile.value || !importProfileId.value) return
  previewing.value = true
  try {
    const r = await bankStatementApi.preview(importTarget.value.id!, importProfileId.value, importFile.value)
    preview.value = r?.data ?? null
  } finally {
    previewing.value = false
  }
}

async function doConfirm() {
  if (!importTarget.value || !importFile.value || !importProfileId.value) return
  confirming.value = true
  try {
    const r = await bankStatementApi.confirm(importTarget.value.id!, importProfileId.value, importFile.value)
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.msg.importDone'))
      importVisible.value = false
      await load()
    } else {
      ElMessage.error(r.message)
    }
  } finally {
    confirming.value = false
  }
}

function goWorkbench(row: BankStatement) {
  router.push({ path: '/fin/bank-reconciliation', query: { id: row.id } })
}

onMounted(load)
</script>

<style scoped>
.bank-statement { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.preview-result { margin-top: 8px; }
</style>
