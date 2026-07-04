<!--
  CpDetailPanel —— 详情描述面板（设计系统 §9.2；详情页「基本信息」等描述区目标模板）。
  按 items 声明以 CSS 栅格铺开「标签 / 值」对；kind 控制值的格式化，与 CpListPage 列约定一致
  （num→数字体右倾 / mono→单号样式 / tag→CpTag / text→原样）。

  Props:
    - items: { label:string; value:unknown; kind?:'text'|'num'|'mono'|'tag' }[]  描述项。
    - cols?: number   栅格列数，默认 2。

  使用示例：
    <CpDetailPanel :cols="3" :items="[
      { label:'单号', value:'SHP-1001', kind:'mono' },
      { label:'数量', value:1000, kind:'num' },
      { label:'状态', value:'已出库', kind:'tag' }
    ]" />
-->
<script lang="ts">
export interface DetailItem {
  label: string
  value: unknown
  kind?: 'text' | 'num' | 'mono' | 'tag'
}
</script>

<script setup lang="ts">
import { computed } from 'vue'
import CpTag from '@/components/base/CpTag.vue'

const props = defineProps<{ items: DetailItem[]; cols?: number }>()

const gridStyle = computed(() => ({
  gridTemplateColumns: `repeat(${props.cols ?? 2}, minmax(0, 1fr))`
}))
</script>

<template>
  <dl class="cp-detail" :style="gridStyle">
    <div v-for="(it, i) in items" :key="i" class="cp-detail-item">
      <dt class="cp-detail-label">{{ it.label }}</dt>
      <dd class="cp-detail-value">
        <CpTag v-if="it.kind === 'tag'" :status="String(it.value ?? '')" />
        <span v-else-if="it.kind === 'mono'" class="cp-mono">{{ it.value }}</span>
        <span v-else-if="it.kind === 'num'" class="num">{{ it.value }}</span>
        <template v-else>{{ it.value }}</template>
      </dd>
    </div>
  </dl>
</template>

<style scoped>
.cp-detail { display:grid; gap:16px 24px; margin:0; }
.cp-detail-item { display:flex; flex-direction:column; gap:5px; min-width:0; }
.cp-detail-label { font-size:var(--cp-fs-2xs); font-weight:800; color:var(--cp-muted); letter-spacing:.5px; }
.cp-detail-value { margin:0; font-size:var(--cp-fs-base); font-weight:700; color:var(--cp-ink);
  overflow:hidden; text-overflow:ellipsis; }
/* 单号样式（同 CpListPage .mono；暂无全局工具类，先在模板内定义） */
.cp-mono { font-weight:800; color:var(--cp-brand-deep); font-size:var(--cp-fs-sm); }
</style>
