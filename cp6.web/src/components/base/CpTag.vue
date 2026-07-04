<!--
  CpTag —— 状态 pill（圆点 + 文字，设计系统 §9.1）。

  Props:
    - status?: string  业务状态词；经 STATUS_TONE 集中映射到色调，空串/未命中均回退 muted。
    - tone?: 'ok'|'warn'|'danger'|'info'|'muted'  显式指定色调，优先级高于 status。
  Slots:
    - default  标签文案；缺省时回退渲染 status 文本。
  Export:
    - STATUS_TONE  状态→色调映射表，供业务侧查询/复用。

  使用示例：
    <CpTag status="已出库">已出库</CpTag>
    <CpTag status="拣货中" />            （info；文案回退 status）
    <CpTag tone="danger">超期</CpTag>     （显式色调）
-->
<script lang="ts">
export const STATUS_TONE: Record<string, string> = {
  '已出库': 'ok', '已出货': 'ok', '已完成': 'ok', '已对账': 'ok', '已批准': 'ok',
  '未出库': 'warn', '未出货': 'warn', '待审批': 'warn', '待处理': 'warn',
  '拣货中': 'info', '进行中': 'info', '已发行': 'info',
  '已取消': 'muted', '已作废': 'muted',
  '超期': 'danger', '今日': 'danger', '已驳回': 'danger'
}
</script>
<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ status?: string; tone?: 'ok'|'warn'|'danger'|'info'|'muted' }>()
// status='' 或未命中 → || 'muted' 兜底（?? 无法拦截空串这类 falsy-非 nullish 值，会漏出 't-' 空色调）
const t = computed(() => props.tone ?? ((props.status && STATUS_TONE[props.status]) || 'muted'))
</script>
<template><span class="cp-tag" :class="`t-${t}`"><slot>{{ status }}</slot></span></template>
<style scoped>
.cp-tag { display:inline-flex; align-items:center; gap:6px; font-size:11.5px; font-weight:800;
  padding:3px 10px; border-radius:999px; white-space:nowrap; }
.cp-tag::before { content:""; width:6px; height:6px; border-radius:50%; background:currentColor; }
.t-ok { background:var(--cp-ok-bg); color:var(--cp-ok); }
.t-warn { background:var(--cp-warn-bg); color:var(--cp-warn); }
.t-danger { background:var(--cp-danger-bg); color:var(--cp-danger); }
.t-info { background:var(--cp-info-bg); color:var(--cp-info); }
.t-muted { background:var(--cp-line-soft); color:var(--cp-muted); }
</style>
