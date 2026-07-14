<!--
  工作日历（年历）管理页（WFS infra ①，A-T4；spec §2）。CpPageShell 壳 + el-calendar #date-cell 自绘。
  空态（后端 isEmpty=true）：提示本租户未维护假日日历 + 一键导入日本法定假日。
  非空态：el-calendar 逐日渲染 CpTag（补班/假日/默认周末），点击某日弹反转对话框（默认/补班/假日 + 备注）。
  年切换：el-calendar 内建月/年导航 → watch 年份变化重新拉取当年例外。零硬编码色（CpTag tone / token）。
  i18n 键走 t()（键文本入库归 F-T1，回退显示键=既定中间态）。
-->
<template>
  <CpPageShell :title="t('oa.workcal.title')" :count="isEmpty ? undefined : items.length">
    <template #actions>
      <el-button :icon="Refresh" circle :loading="loading" @click="reload" />
    </template>

    <!-- 空态：引导导入 -->
    <div v-if="isEmpty" class="wc-empty">
      <el-empty :description="t('oa.workcal.empty')">
        <el-button type="primary" :loading="importing" @click="importJp">
          {{ t('oa.workcal.importJp') }}
        </el-button>
      </el-empty>
    </div>

    <!-- 年历 -->
    <template v-else>
      <div class="wc-legend">
        <CpTag :tone="dayTone('makeup')">{{ t('oa.workcal.legend.makeup') }}</CpTag>
        <CpTag :tone="dayTone('closed')">{{ t('oa.workcal.legend.closed') }}</CpTag>
        <CpTag :tone="dayTone('weekend')">{{ t('oa.workcal.legend.weekend') }}</CpTag>
      </div>

      <el-calendar v-model="calDate">
        <template #date-cell="{ data }">
          <div class="wc-cell" @click="openDay(data.day)">
            <span class="wc-day num">{{ data.day.split('-')[2] }}</span>
            <CpTag
              v-if="hasTag(stateFor(data.day).kind)"
              :tone="dayTone(stateFor(data.day).kind)"
            >{{ t('oa.workcal.kind.' + stateFor(data.day).kind) }}</CpTag>
            <span v-if="stateFor(data.day).note" class="wc-note">{{ stateFor(data.day).note }}</span>
          </div>
        </template>
      </el-calendar>
    </template>

    <!-- 反转对话框 -->
    <el-dialog v-model="dialogVisible" :title="t('oa.workcal.dialog.title') + ' ' + editDay" width="360px">
      <el-radio-group v-model="editKind">
        <el-radio value="normal">{{ t('oa.workcal.kind.normal') }}</el-radio>
        <el-radio value="makeup">{{ t('oa.workcal.kind.makeup') }}</el-radio>
        <el-radio value="closed">{{ t('oa.workcal.kind.closed') }}</el-radio>
      </el-radio-group>
      <el-input
        v-model="editNote"
        class="wc-note-input"
        :placeholder="t('oa.workcal.dialog.note')"
        maxlength="100"
      />
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="saveDay">{{ t('确定') }}</el-button>
      </template>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpTag from '@/components/base/CpTag.vue'
import { workCalendarApi, type WorkCalendarDay } from '@/api/oa/workCalendar'
import {
  toExceptionMap, stateForDate, dayTone, hasTag,
  type DayKind, type DayState,
} from './workCalendarModel'

const { t } = useI18n()

const calDate = ref(new Date())
const items = ref<WorkCalendarDay[]>([])
const isEmpty = ref(false)
const loading = ref(false)
const importing = ref(false)
let exMap = new Map<string, WorkCalendarDay>()

const stateFor = (dayKey: string): DayState => stateForDate(dayKey, exMap)

async function load(year: number) {
  loading.value = true
  try {
    const res = await workCalendarApi.list(year)
    items.value = res.items ?? []
    isEmpty.value = res.isEmpty
    exMap = toExceptionMap(items.value)
  } finally {
    loading.value = false
  }
}

const reload = () => load(calDate.value.getFullYear())

// 年切换：el-calendar 月/年导航改变 calDate → 跨年重拉。
watch(() => calDate.value.getFullYear(), (y, prev) => { if (y !== prev) load(y) })

async function importJp() {
  importing.value = true
  try {
    const { inserted } = await workCalendarApi.importJp()
    ElMessage.success(t('oa.workcal.imported', { n: inserted }))
    await load(calDate.value.getFullYear())
  } finally {
    importing.value = false
  }
}

// ── 反转对话框 ──
const dialogVisible = ref(false)
const saving = ref(false)
const editDay = ref('')
const editKind = ref<DayKind>('normal')
const editNote = ref('')

function openDay(dayKey: string) {
  const st = stateFor(dayKey)
  editDay.value = dayKey
  // weekend 视作默认（清除态），其余照原态
  editKind.value = st.kind === 'weekend' ? 'normal' : st.kind
  editNote.value = st.note ?? ''
  dialogVisible.value = true
}

async function saveDay() {
  saving.value = true
  try {
    if (editKind.value === 'normal') {
      await workCalendarApi.clear(editDay.value)
    } else {
      await workCalendarApi.toggle(editDay.value, editKind.value === 'makeup', editNote.value || null)
    }
    dialogVisible.value = false
    await load(calDate.value.getFullYear())
  } finally {
    saving.value = false
  }
}

onMounted(reload)
</script>

<style scoped>
.wc-empty { padding: 32px 0; }
.wc-legend { display: flex; gap: 10px; flex-wrap: wrap; }
.wc-cell { display: flex; flex-direction: column; gap: 3px; min-height: 46px; cursor: pointer; }
.wc-day { font-weight: 700; }
.wc-note { font-size: 11px; color: var(--cp-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.wc-note-input { margin-top: 12px; }
</style>
