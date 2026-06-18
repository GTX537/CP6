<template>
  <div class="mes-work-center">
    <div class="page-header">
      <h2>{{ t('工作中心') }}</h2>
      <span class="subtitle">{{ t('日可用产能') }} / {{ t('工序费率') }} / {{ t('产能') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-input v-model="keyword" size="small" style="width: 220px" :placeholder="t('工作中心')" clearable @keyup.enter="reload" />
        <el-button type="primary" size="small" @click="reload">{{ t('查询') }}</el-button>
        <el-button size="small" @click="openCreate">{{ t('新增') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" v-loading="loading">
        <el-table-column prop="wgCd" :label="t('工作中心')" width="160" show-overflow-tooltip />
        <el-table-column prop="wgName" :label="t('名称')" min-width="180" show-overflow-tooltip />
        <el-table-column prop="dailyCapacityHours" :label="t('日可用产能')" width="140" align="right" />
        <el-table-column :label="t('启用')" width="90" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="row.enable ? 'success' : 'info'">{{ row.enable ? t('启用') : t('停用') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="140" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('编辑') }}</el-button>
            <el-button link type="danger" size="small" @click="doDelete(row)">{{ t('删除') }}</el-button>
          </template>
        </el-table-column>
        <template #empty><span>{{ t('暂无数据') }}</span></template>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="t('工作中心')" width="520">
      <el-form :model="form" label-width="110px" size="small">
        <el-form-item :label="t('工作中心')" required>
          <el-input v-model="form.wgCd" maxlength="10" :disabled="editing" />
        </el-form-item>
        <el-form-item :label="t('名称')">
          <el-input v-model="form.wgName" maxlength="100" />
        </el-form-item>
        <el-form-item :label="t('日可用产能')">
          <el-input-number v-model="form.dailyCapacityHours" :min="0" :precision="2" controls-position="right" style="width:100%" />
        </el-form-item>
        <el-form-item :label="t('启用')">
          <el-switch v-model="form.enable" />
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
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { workCenterApi } from '@/api/mes/processCost'
import type { WorkCenter } from '@/types/mes/processCost'

const { t } = useI18n()
const rows = ref<WorkCenter[]>([])
const loading = ref(false)
const saving = ref(false)
const keyword = ref('')
const dialogVisible = ref(false)
const editing = ref(false)

function emptyForm(): WorkCenter {
  return { wgCd: '', wgName: '', dailyCapacityHours: 0, enable: true }
}
const form = reactive<WorkCenter>(emptyForm())

async function reload() {
  loading.value = true
  try {
    const res = await workCenterApi.list(keyword.value.trim() || undefined)
    rows.value = res?.data || []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  Object.assign(form, emptyForm())
  editing.value = false
  dialogVisible.value = true
}

function openEdit(row: WorkCenter) {
  Object.assign(form, { ...row, dailyCapacityHours: row.dailyCapacityHours ?? 0 })
  editing.value = true
  dialogVisible.value = true
}

async function submit() {
  if (!form.wgCd?.trim()) { ElMessage.warning(t('工作中心')); return }
  saving.value = true
  try {
    await workCenterApi.save({ ...form, wgCd: form.wgCd.trim() })
    ElMessage.success(t('已保存'))
    dialogVisible.value = false
    await reload()
  } finally {
    saving.value = false
  }
}

async function doDelete(row: WorkCenter) {
  await ElMessageBox.confirm(t('确认删除'), t('提示'), { type: 'warning' })
  await workCenterApi.remove(row.wgCd)
  ElMessage.success(t('已删除'))
  await reload()
}

reload()
</script>

<style scoped>
.mes-work-center { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
