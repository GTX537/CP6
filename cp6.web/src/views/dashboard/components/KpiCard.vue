<script setup lang="ts">
// KpiCard：仪表盘 KPI 卡。trend 传 7 日数值数组时渲染 sparkline（现有 API 无该数据源时不传，不造假数据）。
// 用法：<KpiCard label="在制指令" :value="10" suffix="件" tone="brand" sub="完成率 36.4%"><template #icon><SetUp /></template></KpiCard>
const props = withDefaults(defineProps<{
  label: string
  value: number | string
  suffix?: string
  tone?: 'brand' | 'info' | 'warn' | 'danger'
  trend?: number[]
  sub?: string
  /** 卡片本身是否可点击跳转（由外层容器负责实际 @click/路由）：为 true 时在 sub 行追加一个 → 提示。 */
  clickable?: boolean
}>(), { tone: 'brand' })

type Tone = 'brand' | 'info' | 'warn' | 'danger'

const chipStyle: Record<Tone, { bg: string; color: string }> = {
  brand: { bg: 'var(--cp-brand-bg)', color: 'var(--cp-brand-deep)' },
  info: { bg: 'var(--cp-info-bg)', color: 'var(--cp-info)' },
  warn: { bg: 'var(--cp-warn-bg)', color: 'var(--cp-warn)' },
  danger: { bg: 'var(--cp-danger-bg)', color: 'var(--cp-danger)' },
}
const toneVar: Record<Tone, string> = {
  brand: 'var(--cp-brand)',
  info: 'var(--cp-info)',
  warn: 'var(--cp-warn)',
  danger: 'var(--cp-danger)',
}

function points(t: number[]): string {
  const max = Math.max(...t, 1)
  const min = Math.min(...t, 0)
  return t.map((v, i) => `${(i / (t.length - 1 || 1)) * 100},${28 - ((v - min) / (max - min || 1)) * 24}`).join(' ')
}

function areaPath(t: number[]): string {
  const pts = points(t)
  const coords = pts.split(' ').map(p => {
    const [x, y] = p.split(',')
    return `${x} ${y}`
  })
  return `M${coords[0]} L${coords.slice(1).join(' ')} V30 H0 Z`
}
</script>

<template>
  <div class="kpi cp-hover-lift" :class="{ alert: tone === 'danger', clickable }">
    <div class="top">
      <span class="lbl">{{ label }}</span>
      <span
        v-if="$slots.icon"
        class="chip"
        :style="{ background: chipStyle[props.tone].bg, color: chipStyle[props.tone].color }"
      >
        <slot name="icon" />
      </span>
    </div>
    <div class="val num">{{ value }}<small v-if="suffix"> {{ suffix }}</small></div>
    <div v-if="sub || clickable" class="sub">
      <slot name="sub">{{ sub }}</slot>
      <span v-if="clickable" class="go">→</span>
    </div>
    <svg v-if="trend?.length" class="spark" viewBox="0 0 100 30" preserveAspectRatio="none" aria-hidden="true">
      <path
        :d="areaPath(trend)"
        :fill="{ brand: 'rgba(20,184,196,.10)', info: 'rgba(78,128,238,.10)', warn: 'rgba(240,148,10,.10)', danger: 'rgba(229,72,77,.08)' }[props.tone]"
      />
      <polyline
        :points="points(trend)"
        :stroke="toneVar[props.tone]"
        stroke-width="2"
        fill="none"
        stroke-linecap="round"
      />
    </svg>
  </div>
</template>

<style scoped>
.kpi {
  background: var(--cp-card);
  border-radius: var(--cp-r-lg);
  box-shadow: var(--cp-shadow-1);
  padding: 17px 20px 13px;
  position: relative;
  border: 1px solid transparent;
  overflow: hidden;
}
.kpi .top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.kpi .lbl {
  font-size: var(--cp-fs-xs);
  color: var(--cp-muted);
  font-weight: 800;
  letter-spacing: .6px;
}
.kpi .chip {
  width: 36px;
  height: 36px;
  border-radius: var(--cp-r-md);
  display: grid;
  place-items: center;
  font-size: 17px;
  flex-shrink: 0;
}
.kpi .val {
  font-size: var(--cp-fs-num-lg);
  font-weight: 800;
  color: var(--cp-ink);
  line-height: 1.05;
  margin: 8px 0 2px;
  letter-spacing: -.5px;
}
.kpi .val small {
  font-size: var(--cp-fs-base);
  color: var(--cp-muted);
  font-weight: 700;
  letter-spacing: 0;
}
.kpi .sub {
  font-size: var(--cp-fs-xs);
  color: var(--cp-muted);
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 5px;
  margin-bottom: 4px;
}
.kpi.alert {
  border-color: rgba(229, 72, 77, .25);
}
.kpi.alert .val {
  color: var(--cp-danger);
}
.kpi.clickable {
  cursor: pointer;
}
.kpi .go {
  margin-left: auto;
  color: var(--cp-brand-deep);
  font-weight: 800;
}
.kpi.alert .go {
  color: var(--cp-danger);
}
.spark {
  display: block;
  width: 100%;
  height: 30px;
  margin-top: 2px;
}
</style>
