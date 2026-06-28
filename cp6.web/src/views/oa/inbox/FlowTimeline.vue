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
            <span class="tl-node">{{ row.nodeName || row.nodeId }}</span>

            <!-- Persisted row (forecast === false) -->
            <template v-if="!row.forecast">
              <el-tag
                size="small"
                :type="statusTagType(row.status)"
                style="margin-left: 6px"
              >
                {{ t(formToStatusText(row.status ?? 0)) }}
              </el-tag>
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
    <el-empty
      v-if="branchEntries.length === 0"
      :image-size="60"
      :description="t('暂无流转记录')"
    />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TimelineRow, ForecastStep } from '@/types/oa/inbox'
import { mergeTimeline, groupByBranch, formToStatusText, type MergedRow } from './inboxModel'

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
  if (status === 2) return 'danger'
  if (status === 0) return 'warning'
  return 'info'
}

function statusTagType(
  status?: number,
): 'success' | 'warning' | 'danger' | 'info' {
  if (status === 1) return 'success'
  if (status === 2) return 'danger'
  if (status === 0) return 'warning'
  return 'info'
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
  color: var(--el-color-primary);
  margin: 8px 0 4px;
  padding-left: 6px;
  border-left: 3px solid var(--el-color-primary-light-5);
}

.tl-row {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.tl-node {
  font-weight: 600;
  font-size: 13px;
}

.tl-handler {
  font-size: 12px;
  color: var(--el-text-color-regular);
}

.tl-comment {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  font-style: italic;
  margin: 0;
}

.tl-time {
  font-size: 11px;
  color: var(--el-text-color-placeholder);
}

.tl-approvers {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* Forecast items: dashed tail + muted text */
.forecast-item :deep(.el-timeline-item__tail) {
  border-left-style: dashed;
}

.forecast-item :deep(.el-timeline-item__content) {
  color: var(--el-text-color-secondary);
}
</style>
