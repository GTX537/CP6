<template>
  <CpPageShell :title="tr('space.planningScenario.pageTitle', '规划方案')">
    <template #actions>
      <el-select
        v-model="siteId"
        filterable
        :placeholder="tr('space.planningScenario.chooseSite', '选择站点')"
        style="width: 280px"
      >
        <el-option
          v-for="site in sites"
          :key="site.id"
          :value="site.id"
          :label="`${site.siteCode} · ${site.siteName}`"
        />
      </el-select>
    </template>

    <CpEmpty
      v-if="!siteId"
      :text="tr('space.planningScenario.chooseSiteFirst', '请先选择一个站点。')"
    />
    <PlanningScenarioPanel
      v-else
      :site-id="siteId"
      :base-published-version-id="model?.currentPublishedVersionId"
    />
  </CpPageShell>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { planningScenarioApi } from '@/api/space/planningScenario'
import { siteApi } from '@/api/space/site'
import { useTOr } from '@/i18n/tOr'
import PlanningScenarioPanel from './PlanningScenarioPanel.vue'
import type { SpaceDesignModel } from '@/api/space/planningScenario'
import type { SiteVO } from '@/types/space/scene'

const tr = useTOr()
const route = useRoute()
const sites = ref<SiteVO[]>([])
const siteId = ref(typeof route.query.siteId === 'string' ? route.query.siteId : '')
const model = ref<SpaceDesignModel | null>(null)
let modelSequence = 0

onMounted(async () => {
  const response = await siteApi.list()
  sites.value = response.data || []
})

watch(siteId, async value => {
  const sequence = ++modelSequence
  model.value = null
  if (!value) return
  try {
    const result = await planningScenarioApi.getModel(value)
    if (sequence === modelSequence) model.value = result
  } catch {
    if (sequence === modelSequence) model.value = null
  }
}, { immediate: true })
</script>
