<template>
  <div class="gl-account">
    <div class="page-header">
      <h2>{{ t('会计科目') }}</h2>
      <span class="subtitle">{{ t('总账科目表（多国别模板包 + 角色锚点）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="scheme" size="small" style="width: 200px" @change="reload">
          <el-option v-for="s in SCHEME_OPTIONS" :key="s.value" :value="s.value" :label="`${t(s.label)} (${s.value})`" />
        </el-select>
        <el-checkbox v-model="includeInactive" size="small" @change="reload">{{ t('含停用') }}</el-checkbox>
        <el-button type="primary" size="small" @click="openCreate">{{ t('新建') }}</el-button>
        <el-button size="small" @click="doImport">{{ t('导入模板') }}</el-button>
        <el-button size="small" @click="reload">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="code" :label="t('编码')" width="110" sortable />
        <el-table-column prop="name" :label="t('名称')" min-width="180" show-overflow-tooltip />
        <el-table-column :label="t('类型')" width="90">
          <template #default="{ row }">{{ t(ACCOUNT_TYPE_LABEL[row.type] || '') }}</template>
        </el-table-column>
        <el-table-column :label="t('方向')" width="70">
          <template #default="{ row }">{{ t(ACCOUNT_SIDE_LABEL[row.normalSide] || '') }}</template>
        </el-table-column>
        <el-table-column :label="t('末级')" width="70" align="center">
          <template #default="{ row }"><el-tag v-if="row.isLeaf" size="small" type="success">{{ t('是') }}</el-tag></template>
        </el-table-column>
        <el-table-column :label="t('控制科目')" width="120" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.isControl" size="small" type="warning">{{ row.subLedgerType }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="role" :label="t('角色锚点')" width="150" show-overflow-tooltip />
        <el-table-column :label="t('状态')" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'" size="small">{{ row.isActive ? t('启用') : t('停用') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('编辑') }}</el-button>
            <el-button v-if="row.isActive" link type="danger" size="small" @click="deactivate(row)">{{ t('停用') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editingId ? t('编辑科目') : t('新建科目')" width="520">
      <el-form :model="form" label-width="110px">
        <el-form-item :label="t('编码')" required>
          <el-input v-model="form.code" :disabled="!!editingId" maxlength="30" />
        </el-form-item>
        <el-form-item :label="t('名称')" required>
          <el-input v-model="form.name" maxlength="100" />
        </el-form-item>
        <el-form-item :label="t('类型')" required>
          <el-select v-model="form.type" style="width: 100%">
            <el-option v-for="(lbl, k) in ACCOUNT_TYPE_LABEL" :key="k" :value="Number(k)" :label="t(lbl)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('正常方向')" required>
          <el-radio-group v-model="form.normalSide">
            <el-radio :value="1">{{ t('借') }}</el-radio>
            <el-radio :value="2">{{ t('贷') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('末级科目')">
          <el-switch v-model="form.isLeaf" />
          <span class="hint">{{ t('只有末级科目能记凭证') }}</span>
        </el-form-item>
        <el-form-item :label="t('控制科目')">
          <el-switch v-model="form.isControl" />
        </el-form-item>
        <el-form-item v-if="form.isControl" :label="t('子账类型')">
          <el-input v-model="form.subLedgerType" maxlength="10" placeholder="AP / AR / INV / COST" />
        </el-form-item>
        <el-form-item :label="t('必填往来单位')">
          <el-switch v-model="form.requirePartner" />
        </el-form-item>
        <el-form-item :label="t('角色锚点')">
          <el-input v-model="form.role" maxlength="30" placeholder="AP_CONTROL / REVENUE ..." />
        </el-form-item>
        <el-form-item :label="t('币种')">
          <el-input v-model="form.currencyCd" maxlength="10" :placeholder="t('留空=本位币')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false" :disabled="saving">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="submit">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { glAccountApi } from '@/api/fin/fin'
import {
  ACCOUNT_TYPE_LABEL, ACCOUNT_SIDE_LABEL, SCHEME_OPTIONS, type GlAccount,
} from '@/types/fin/fin'

const { t } = useI18n()

const rows = ref<GlAccount[]>([])
const loading = ref(false)
const saving = ref(false)
const scheme = ref('CN-GAAP')
const includeInactive = ref(false)
const dialogVisible = ref(false)
const editingId = ref<string | null>(null)

function emptyForm(): GlAccount {
  return {
    code: '', name: '', type: 1, normalSide: 1, level: 1, isLeaf: true,
    isControl: false, subLedgerType: null, requirePartner: false, role: null,
    standardScheme: scheme.value, isActive: true, currencyCd: null,
  }
}
const form = reactive<GlAccount>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await glAccountApi.list(scheme.value, includeInactive.value)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

function openEdit(row: GlAccount) {
  editingId.value = row.id || null
  Object.assign(form, { ...emptyForm(), ...row })
  dialogVisible.value = true
}

async function submit() {
  if (!form.code?.trim() || !form.name?.trim()) {
    ElMessage.warning(t('编码和名称必填'))
    return
  }
  saving.value = true
  try {
    if (editingId.value) {
      await glAccountApi.update(editingId.value, { ...form })
      ElMessage.success(t('已保存'))
    } else {
      await glAccountApi.create({ ...form })
      ElMessage.success(t('已新建'))
    }
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function deactivate(row: GlAccount) {
  try {
    await ElMessageBox.confirm(t('确认停用科目「{name}」？', { name: row.name }), t('停用'), { type: 'warning' })
  } catch { return }
  if (!row.id) return
  await glAccountApi.deactivate(row.id)
  ElMessage.success(t('已停用'))
  await reload()
}

async function doImport() {
  try {
    await ElMessageBox.confirm(
      t('将导入 {scheme} 科目表模板（已导入过会被拒绝）。继续？', { scheme: scheme.value }),
      t('导入模板'), { type: 'warning' },
    )
  } catch { return }
  const res = await glAccountApi.importTemplate(scheme.value)
  ElMessage.success(t('已导入 {n} 个科目', { n: res?.data?.count ?? 0 }))
  await reload()
}

onMounted(reload)
</script>

<style scoped>
.gl-account { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.hint { color: #909399; font-size: 12px; margin-left: 8px; }
</style>
