<script setup lang="ts">
// DonutChart：环图 + 右侧图例。segments 为空（或合计为 0）时渲染灰色整环占位。
import { computed } from 'vue'

interface Segment { label: string; value: number; color: string }

const props = defineProps<{
  segments: Segment[]
  centerLabel: string
}>()

const R = 46
const C = 2 * Math.PI * R // ≈ 289.03
const GAP = 3

const total = computed(() => props.segments.reduce((sum, s) => sum + (s.value || 0), 0))

interface Arc { label: string; value: number; color: string; dasharray: string; dashoffset: number; pct: number }

const arcs = computed<Arc[]>(() => {
  if (!props.segments.length || total.value <= 0) return []
  let acc = 0
  return props.segments
    .filter(s => (s.value || 0) > 0)
    .map((s) => {
      const raw = (s.value / total.value) * C
      const len = Math.max(raw - GAP, 0)
      const arc: Arc = {
        label: s.label,
        value: s.value,
        color: s.color,
        dasharray: `${len} ${C}`,
        dashoffset: -acc,
        pct: (s.value / total.value) * 100,
      }
      acc += raw
      return arc
    })
})

const isEmpty = computed(() => arcs.value.length === 0)
</script>

<template>
  <div class="donut-wrap">
    <div class="donut">
      <svg width="118" height="118" viewBox="0 0 118 118">
        <circle cx="59" cy="59" :r="R" fill="none" stroke="var(--cp-line-soft)" stroke-width="13" />
        <circle
          v-if="isEmpty"
          cx="59" cy="59" :r="R" fill="none" stroke="var(--cp-faint)" stroke-width="13"
        />
        <circle
          v-for="(a, i) in arcs"
          :key="i"
          cx="59" cy="59" :r="R" fill="none"
          :stroke="a.color"
          stroke-width="13"
          stroke-linecap="round"
          :stroke-dasharray="a.dasharray"
          :stroke-dashoffset="a.dashoffset"
        />
      </svg>
      <div class="center">
        <b class="num">{{ isEmpty ? '-' : total }}</b>
        <span>{{ centerLabel }}</span>
      </div>
    </div>
    <div class="lgs">
      <div v-for="(a, i) in arcs" :key="i" class="lg">
        <span class="sq" :style="{ background: a.color }" />
        {{ a.label }}
        <b class="num">{{ a.value }}</b>
        <span class="pct num">{{ a.pct.toFixed(1) }}%</span>
      </div>
      <slot v-if="isEmpty" name="empty" />
    </div>
  </div>
</template>

<style scoped>
.donut-wrap {
  display: flex;
  align-items: center;
  gap: 20px;
  padding: 18px 20px;
}
.donut {
  position: relative;
  width: 118px;
  height: 118px;
  flex-shrink: 0;
}
.donut svg {
  transform: rotate(-90deg);
}
.donut .center {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
}
.donut .center b {
  font-size: 26px;
  font-weight: 800;
  color: var(--cp-ink);
  line-height: 1;
}
.donut .center span {
  font-size: var(--cp-fs-2xs);
  color: var(--cp-muted);
  font-weight: 800;
  letter-spacing: .8px;
  margin-top: 3px;
  text-align: center;
}
.lgs {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 11px;
  min-width: 0;
}
.lg {
  display: flex;
  align-items: center;
  gap: 9px;
  font-size: var(--cp-fs-base);
  font-weight: 700;
  color: var(--cp-text);
}
.lg .sq {
  width: 8px;
  height: 8px;
  border-radius: 2.5px;
  flex-shrink: 0;
}
.lg b {
  margin-left: auto;
  color: var(--cp-ink);
  font-weight: 800;
}
.lg .pct {
  color: var(--cp-muted);
  font-size: var(--cp-fs-xs);
  width: 48px;
  text-align: right;
  font-weight: 700;
}
</style>
