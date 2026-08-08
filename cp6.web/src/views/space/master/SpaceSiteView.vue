<!--
  站点マスタ —— CpPageShell + CpListPage + CpFormDialog（Space 波2）。
  1:1 套用 WarehouseListView 范式：单表 CRUD、前端切片分页（list 端点无分页）、
  in-place 变更后 listRef.reload() 命令式刷新（保留筛选/页码）。
  warehouseCd 列空显示「—」并 title 提示发布时默认取站点编码；enable 走 kind:tag map。
  编辑时 siteCode 只读（发布后 code 是映射锚——保守禁改，波1.5 终审 M 项背书）。
  删除失败的后端护栏 message 由 http 拦截器原样 ElMessage.error（此处 catch 仅止崩）。
-->
<template>
  <CpPageShell :title="t('space.site.title')" :count="total">
    <template #actions>
      <el-button v-permission="'space-site:add'" @click="openCreate">{{ t('space.site.create') }}</el-button>
    </template>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      :search-fields="searchFields"
      :filter-labels="filterLabels"
      @total-change="total = $event"
    >
      <template #col-warehouseCd="{ row }">
        <span v-if="row.warehouseCd" class="cp-mono">{{ row.warehouseCd }}</span>
        <span v-else class="cp-dash" :title="t('space.site.whDefaultTip', { code: row.siteCode })">—</span>
      </template>
      <template #col-_action="{ row }">
        <el-button v-permission="'space-site:edit'" link type="primary" size="small" @click="openEdit(row)">{{ t('space.common.edit') }}</el-button>
        <el-button link type="primary" size="small" @click="gotoFloor(row)">{{ t('space.site.floors') }}</el-button>
        <el-button v-permission="'space-site:delete'" link type="danger" size="small" @click="onDelete(row)">{{ t('space.common.delete') }}</el-button>
      </template>
    </CpListPage>

    <CpFormDialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="560"
      :form="form"
      :rules="rules"
      :submit="onSave"
      :labels="{ cancel: t('space.common.cancel'), confirm: t('space.common.save') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('space.site.fld.code')" prop="siteCode">
        <el-input v-model="form.siteCode" :disabled="!!form.id" maxlength="50" />
      </el-form-item>
      <el-form-item :label="t('space.site.fld.name')" prop="siteName">
        <el-input v-model="form.siteName" maxlength="100" />
      </el-form-item>
      <el-form-item :label="t('space.site.fld.warehouseCd')" prop="warehouseCd">
        <el-input v-model="form.warehouseCd" maxlength="10" :placeholder="t('space.site.whDefault')" />
      </el-form-item>
      <el-form-item :label="t('space.site.fld.address')">
        <el-input v-model="form.address" maxlength="200" />
      </el-form-item>
      <el-form-item :label="t('space.common.status')">
        <el-switch v-model="form.enable" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import { siteApi } from '@/api/space/site'
import type { SiteVO } from '@/types/space/scene'

const { t } = useI18n()
const router = useRouter()

const total = ref<number>()
// in-place 变更后命令式刷新（保留当前筛选/页码）
const listRef = ref<ListPageExpose | null>(null)
function reloadList() { listRef.value?.reload() }

const columns = computed<ListColumn<SiteVO>[]>(() => [
  { prop: 'siteCode', label: t('space.site.fld.code'), width: 160, kind: 'mono' },
  { prop: 'siteName', label: t('space.site.fld.name'), minWidth: 200 },
  { prop: 'warehouseCd', label: t('space.site.fld.warehouseCd'), width: 140 },
  { prop: 'address', label: t('space.site.fld.address'), minWidth: 220, overflowTooltip: true },
  { prop: 'enable', label: t('space.common.status'), width: 110, align: 'center', kind: 'tag',
    map: (v) => (v
      ? { label: t('space.common.enabled'), tone: 'ok' }
      : { label: t('space.common.disabled'), tone: 'muted' }) },
  { prop: '_action', label: t('space.common.action'), width: 200, fixed: 'right' },
])

const filterLabels = computed(() => ({
  search: t('space.common.search'),
  reset: t('space.common.clear'),
}))

const searchFields = computed<FilterField[]>(() => [
  { key: 'siteCode', label: t('space.site.fld.code'), type: 'text' },
  { key: 'siteName', label: t('space.site.fld.name'), type: 'text' },
])

// list 端点无分页/筛选 → 前端拉全量、切片分页（照 WarehouseListView 切片写法）
const fetchList: ListFetch<SiteVO> = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const res = await siteApi.list()
  let all = res.data || []
  if (f.siteCode) {
    const kw = String(f.siteCode).toLowerCase()
    all = all.filter((r) => (r.siteCode || '').toLowerCase().includes(kw))
  }
  if (f.siteName) {
    const kw = String(f.siteName).toLowerCase()
    all = all.filter((r) => (r.siteName || '').toLowerCase().includes(kw))
  }
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建/編集对话框 ——
const dialogVisible = ref(false)
const form = reactive<SiteVO>({ siteCode: '', siteName: '', warehouseCd: '', address: '', enable: true })
const dialogTitle = computed(() => (form.id ? t('space.site.dlg.edit') : t('space.site.dlg.create')))
const rules = computed<FormRules>(() => ({
  siteCode: [{ required: true, message: t('space.common.required'), trigger: 'blur' }],
  siteName: [{ required: true, message: t('space.common.required'), trigger: 'blur' }],
}))

function openCreate() {
  Object.assign(form, { id: undefined, siteCode: '', siteName: '', warehouseCd: '', address: '', enable: true })
  dialogVisible.value = true
}
function openEdit(row: SiteVO) {
  Object.assign(form, { warehouseCd: '', address: '', ...row })
  dialogVisible.value = true
}

async function onSave() {
  if (form.id) {
    await siteApi.update(form.id, { ...form })
  } else {
    await siteApi.create({ ...form })
  }
  ElMessage.success(t('space.common.success'))
}

async function onDelete(row: SiteVO) {
  try {
    await ElMessageBox.confirm(`${t('space.common.confirmDelete')} [${row.siteCode}]`, t('space.common.confirm'), { type: 'warning' })
    await siteApi.remove(row.id!)
    ElMessage.success(t('space.common.success'))
    reloadList()
  } catch { /* 取消 / 后端子级护栏错误（后者已由 http 拦截器原样提示） */ }
}

function gotoFloor(row: SiteVO) {
  router.push({ path: '/space/floor', query: { siteId: row.id } })
}
</script>

<style scoped>
.cp-dash { color: var(--cp-muted); }
</style>
