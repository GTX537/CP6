<template>
  <div class="flow-timeline">
    <template v-for="([branchKey, branchRows], bi) in branchEntries" :key="branchKey">
      <div v-if="branchEntries.length > 1" class="branch-header">
        {{ t('分支') }} {{ bi + 1 }}
      </div>
      <el-timeline>
        <el-timeline-item
          v-for="(row, i) in branchRows"
          :key="i"
          :type="row.forecast ? 'info' : timelineType(row.status)"
          :hollow="row.forecast"
          :class="{ 'forecast-item': row.forecast }"
        >
          <div class="tl-row">
            <div class="tl-node-line">
              <span class="tl-node">{{ row.nodeName || row.nodeId }}</span>
              <span v-if="row.stageIndex != null" class="tl-stage-label">
                第 {{ (row.stageIndex ?? 0) + 1 }} 档 · 第 {{ (row.stageRound ?? 0) + 1 }} 轮
              </span>
            </div>

            <!-- Persisted row (forecast === false) -->
            <template v-if="!row.forecast">
              <div style="display:flex; gap:4px; align-items:center;">
                <CpTag :tone="formToStatusTone(row.status ?? 0)">
                  {{ t(formToStatusText(row.status ?? 0)) }}
                </CpTag>
                <CpTag v-if="row.status === 7" tone="danger">
                  {{ t('oa.timeline.sentBack') }}
                </CpTag>
              </div>
              <div class="tl-handler">
                <template v-if="row.actualHandlerName">
                  {{ t('实办') }}：{{ row.actualHandlerName }}
                  <span v-if="row.onBehalfOfName">
                    （代 {{ row.onBehalfOfName }} 签）
                  </span>
                </template>
                <template v-else>
                  {{ t('应办') }}：{{ row.expectedHandlerName }}
                </template>
              </div>
              <div v-if="row.comment" class="tl-comment">{{ row.comment }}</div>
              <div class="tl-time">{{ formatTime(row.handledAt || row.sentAt) }}</div>
            </template>

            <!-- Forecast row (forecast === true) -->
            <template v-else>
              <div class="tl-approvers">
                {{
                  row.resolved
                    ? (row.approvers ?? []).join('、')
                    : t('审批人到达时解析')
                }}
              </div>
              <div v-if="forecastNoteMap.get(row.nodeId)" class="tl-comment">
                {{ forecastNoteMap.get(row.nodeId) }}
              </div>
            </template>
          </div>
        </el-timeline-item>
      </el-timeline>
    </template>
    <CpEmpty
      v-if="branchEntries.length === 0"
      :text="t('暂无流转记录')"
    />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TimelineRow, ForecastStep } from '@/types/oa/inbox'
import { mergeTimeline, groupByBranch, formToStatusText, type MergedRow } from './inboxModel'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'

const props = defineProps<{
  timeline: TimelineRow[]
  forecast: ForecastStep[]
}>()

const { t } = useI18n()

const mergedRows = computed<MergedRow[]>(() =>
  mergeTimeline(props.timeline, props.forecast),
)

const branches = computed(() => groupByBranch(mergedRows.value))

const branchEntries = computed((): Array<[string, MergedRow[]]> =>
  [...branches.value.entries()],
)

/** nodeId → note for forecast steps that carry a note */
const forecastNoteMap = computed(() => {
  const map = new Map<string, string>()
  for (const f of props.forecast) {
    if (f.note) map.set(f.nodeId, f.note)
  }
  return map
})

type TimelineType = 'primary' | 'success' | 'warning' | 'danger' | 'info'

function timelineType(status?: number): TimelineType {
  if (status === 1) return 'success'
  if (status === 2 || status === 7) return 'danger'
  if (status === 0) return 'warning'
  return 'info'
}

/**
 * 关卡状态码 → CpTag 色調（FlowFormToStatus 0..7；7=SentBack）。
 * 0-6 与 InboxDone.formToStatusTone 对齐（warn/ok/danger/info…），7=danger，
 * 保留原 statusTagType 视觉（0→warn·1→ok·2/7→danger·其余 info）。
 */
function formToStatusTone(status?: number): Tone {
  return (['warn', 'ok', 'danger', 'info', 'info', 'info', 'info', 'danger'] as Tone[])[status ?? 0] ?? 'info'
}

function formatTime(s?: string): string {
  if (!s) return ''
  return s.replace('T', ' ').slice(0, 19)
}
</script>

<style scoped>
.flow-timeline {
  padding: 4px 0;
}

.branch-header {
  font-size: 12px;
  font-weight: 600;
  color: var(--cp-brand-deep);
  margin: 8px 0 4px;
  padding-left: 6px;
  border-left: 3px solid var(--cp-brand);
}

.tl-row {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.tl-node-line {
  display: flex;
  align-items: center;
  gap: 6px;
}

.tl-node {
  font-weight: 600;
  font-size: 13px;
}

.tl-stage-label {
  font-size: 11px;
  color: var(--cp-brand-deep);
  font-weight: 500;
  background: var(--cp-brand-bg);
  border-radius: var(--cp-r-sm);
  padding: 1px 5px;
}

.tl-handler {
  font-size: 12px;
  color: var(--cp-text);
}

.tl-comment {
  font-size: 12px;
  color: var(--cp-muted);
  font-style: italic;
  margin: 0;
}

.tl-time {
  font-size: 11px;
  color: var(--cp-faint);
}

.tl-approvers {
  font-size: 12px;
  color: var(--cp-muted);
}

/* Forecast items: dashed tail + muted text */
.forecast-item :deep(.el-timeline-item__tail) {
  border-left-style: dashed;
}

.forecast-item :deep(.el-timeline-item__content) {
  color: var(--cp-muted);
}
</style>
