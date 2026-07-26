<!--
  楼层マスタ —— CpPageShell + CpListPage + CpFormDialog（Space 波2）。
  1:1 套用 SpaceSiteView 范式（单表 CRUD、前端切片分页、in-place 变更后 listRef.reload()）；
  差异在「站点上下文」：顶部工具栏站点下拉（siteApi.list 填充、route.query.siteId 预选）；
  未选站点显示 CpEmpty，选定后才渲染列表；站点间切换命令式 reload（首次选定由 v-if 挂载自动首查）。
  underlay/origin 属编辑器域，不进本表单（YAGNI）；新建时 siteId 由当前选中站点隐式带入。
  「編集画面」named-push {name:'space-editor', params:{floorId}} 进全屏编辑器。
  删除失败的后端护栏 message 由 http 拦截器原样 ElMessage.error（此处 catch 仅止崩）。
-->
<template>
  <CpPageShell :title="t('space.floor.title')" :count="total">
    <template #actions>
      <el-select
        v-model="selectedSiteId"
        :placeholder="t('space.floor.selectSite')"
        filterable
        style="width: 240px"
      >
        <el-option
          v-for="s in sites"
          :key="s.id"
          :label="`${s.siteCode}　${s.siteName}`"
          :value="s.id!"
        />
      </el-select>
      <el-button v-permission="'space-floor:add'" :disabled="!selectedSiteId" @click="openCreate">{{ t('space.floor.create') }}</el-button>
    </template>

    <CpListPage
      v-if="selectedSiteId"
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      @total-change="total = $event"
    >
      <template #col-_action="{ row }">
        <el-button v-permission="'space-floor:edit'" link type="primary" size="small" @click="openEdit(row)">{{ t('space.common.edit') }}</el-button>
        <el-button v-permission="'space-floor:delete'" link type="danger" size="small" @click="onDelete(row)">{{ t('space.common.delete') }}</el-button>
        <el-button link type="primary" size="small" @click="gotoEditor(row)">{{ t('space.floor.editor') }}</el-button>
      </template>
    </CpListPage>
    <CpEmpty v-else :text="t('space.floor.pickSiteFirst')" />

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
      <el-form-item :label="t('space.floor.fld.level')" prop="level">
        <el-input-number v-model="form.level" :min="0" :step="1" :precision="0" controls-position="right" />
      </el-form-item>
      <el-form-item :label="t('space.floor.fld.floorCode')" prop="floorCode">
        <el-input v-model="form.floorCode" maxlength="50" />
      </el-form-item>
      <el-form-item :label="t('space.floor.fld.floorName')" prop="floorName">
        <el-input v-model="form.floorName" maxlength="100" />
      </el-form-item>
      <el-form-item :label="t('space.floor.fld.height')" prop="height">
        <el-input-number v-model="form.height" :min="0" :step="500" :precision="0" controls-position="right" />
      </el-form-item>
    </CpFormDialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch, nextTick } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import type { SiteVO, FloorVO } from '@/types/space/scene'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()

const total = ref<number>()
// in-place 变更后命令式刷新（保留当前筛选/页码）
const listRef = ref<ListPageExpose | null>(null)
function reloadList() { listRef.value?.reload() }

// —— 站点上下文 ——
const sites = ref<SiteVO[]>([])
const selectedSiteId = ref<string | undefined>()

onMounted(async () => {
  const res = await siteApi.list()
  sites.value = res.data || []
  const qid = route.query.siteId
  if (typeof qid === 'string' && qid) selectedSiteId.value = qid // 预选（站点页「楼层」跳转带入）
})

// 站点切换：站点→站点时命令式 reload；首次选定（undefined→id）由 v-if 挂载自动首查，无需重复。
watch(selectedSiteId, (nv, ov) => {
  if (nv && ov) nextTick(reloadList)
})

const columns = computed<ListColumn<FloorVO>[]>(() => [
  { prop: 'level', label: t('space.floor.fld.level'), width: 100, kind: 'num' },
  { prop: 'floorCode', label: t('space.floor.fld.floorCode'), width: 160, kind: 'mono' },
  { prop: 'floorName', label: t('space.floor.fld.floorName'), minWidth: 200 },
  { prop: 'height', label: t('space.floor.fld.height'), width: 140, kind: 'num' },
  { prop: '_action', label: t('space.common.action'), width: 220, fixed: 'right' },
])

// list 端点按 siteId 返回全量 → 前端切片分页（照 SpaceSiteView 切片写法）
const fetchList: ListFetch<FloorVO> = async ({ page, size }) => {
  if (!selectedSiteId.value) return { rows: [], total: 0 }
  const res = await floorApi.list(selectedSiteId.value)
  const all = res.data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建/編集对话框 —— form 为 Partial（新建无 id / underlay·origin 不进表单）
const dialogVisible = ref(false)
const form = reactive<Partial<FloorVO>>({ level: undefined, floorCode: '', floorName: '', height: 6000 })
const dialogTitle = computed(() => (form.id ? t('space.floor.dlg.edit') : t('space.floor.dlg.create')))
const rules = computed<FormRules>(() => ({
  level: [{ required: true, message: t('space.common.required'), trigger: 'blur' }],
  floorCode: [{ required: true, message: t('space.common.required'), trigger: 'blur' }],
  floorName: [{ required: true, message: t('space.common.required'), trigger: 'blur' }],
}))

function openCreate() {
  Object.assign(form, { id: undefined, level: undefined, floorCode: '', floorName: '', height: 6000 })
  dialogVisible.value = true
}
function openEdit(row: FloorVO) {
  Object.assign(form, { ...row }) // 保留 underlay/origin（更新时回传，避免编辑器域被清零）
  dialogVisible.value = true
}

async function onSave() {
  // 只取表单四字段 + 当前站点；编辑时并入原行其余字段（underlay/origin）后回传。
  const editable: Partial<FloorVO> = {
    siteId: selectedSiteId.value,
    level: form.level,
    floorCode: form.floorCode,
    floorName: form.floorName,
    height: form.height,
  }
  if (form.id) {
    await floorApi.update(form.id, { ...form, ...editable } as FloorVO)
  } else {
    await floorApi.create(editable)
  }
  ElMessage.success(t('space.common.success'))
}

async function onDelete(row: FloorVO) {
  try {
    await ElMessageBox.confirm(`${t('space.common.confirmDelete')} [${row.floorCode}]`, t('space.common.confirm'), { type: 'warning' })
    await floorApi.remove(row.id)
    ElMessage.success(t('space.common.success'))
    reloadList()
  } catch { /* 取消 / 后端子级护栏错误（后者已由 http 拦截器原样提示） */ }
}

function gotoEditor(row: FloorVO) {
  router.push({ name: 'space-editor', params: { floorId: row.id } })
}
</script>

<style scoped>
</style>
