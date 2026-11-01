<template>
  <div>
    <SchedulingBoardPanel />
    <SchedulingTimePanel /><SchedulingArrangementPanel /><SchedulingQueuePanel /><SchedulingRebindPanel />
    <button @click="legacy = !legacy">{{ legacy ? '关闭历史参考' : '打开旧系统只读参考' }}</button>
    <LegacyPlanningBoardPanel v-if="legacy" />
  </div>
</template>
<script setup lang="ts">
import { ref } from 'vue'
import SchedulingBoardPanel from './SchedulingBoardPanel.vue'
import SchedulingTimePanel from './SchedulingTimePanel.vue'
import SchedulingArrangementPanel from './SchedulingArrangementPanel.vue'
import SchedulingQueuePanel from './SchedulingQueuePanel.vue'
import SchedulingRebindPanel from './SchedulingRebindPanel.vue'
import LegacyPlanningBoardPanel from './LegacyPlanningBoardPanel.vue'
const legacy = ref(false)
</script>
