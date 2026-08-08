<!--
  Space 落地页 —— 站点/楼层导航入口（Space 波2；消灭 3D 编辑器/浏览器/层叠视图三条孤儿 standalone 路由）。
  范式=WmsDashboardView 的 CpStatCard 网格 + CpSectionHeader 卡片区（不套 el-card #header 插槽，改用普通 div，
  便于单测直取——本页无表格）。数据：siteApi.list 拉全站点，再 Promise.all 各站点 floorApi.list 汇总楼层。
  跳 standalone 页一律 named-push：space-viewer(params.siteId[,query.floorId]) / space-stacked / space-editor(params.floorId)。
  无站点 → CpEmpty +「去创建站点」push /space/site（普通菜单路由，非 standalone）。
  自写标签 cp-mono 须本组件补 scoped 类（SpaceSiteView 前车之鉴：slot 内 cp-mono 因 scoped 隔离而失样）。
-->
<template>
  <div class="space-home">
    <div class="stat-grid">
      <CpStatCard :label="t('space.home.siteCount')" :value="sites.length" tone="brand" />
      <CpStatCard :label="t('space.home.floorCount')" :value="floorCount" tone="brand" />
    </div>

    <template v-if="sites.length">
      <div v-for="s in sites" :key="s.id" class="site-card">
        <CpSectionHeader :title="s.siteName">
          <span class="cp-mono site-code">{{ s.siteCode }}</span>
          <template #extra>
            <el-button link type="primary" size="small" @click="gotoViewer(s)">{{ t('space.home.viewer3d') }}</el-button>
            <el-button link type="primary" size="small" @click="gotoStacked(s)">{{ t('space.home.stacked') }}</el-button>
            <el-button
              v-permission="'space:planning:scenario:read'"
              link
              type="primary"
              size="small"
              @click="gotoPlanning(s)"
            >规划方案</el-button>
          </template>
        </CpSectionHeader>

        <div class="floor-body">
          <div v-if="!floorMap[s.id!]?.length" class="floor-empty">
            {{ t('space.home.noFloor') }}
          </div>
          <div v-for="f in floorMap[s.id!]" :key="f.id" class="floor-row">
            <span class="floor-label"><span class="cp-mono">L{{ f.level }}</span> {{ f.floorName }}</span>
            <span class="floor-actions">
              <el-button link type="primary" size="small" @click="gotoEditor(f)">{{ t('space.common.edit') }}</el-button>
              <el-button link type="primary" size="small" @click="gotoFloorViewer(s, f)">{{ t('space.home.viewer3d') }}</el-button>
            </span>
          </div>
        </div>
      </div>
    </template>

    <CpEmpty v-else :text="t('space.home.empty')">
      <template #action>
        <el-button type="primary" @click="gotoCreateSite">{{ t('space.home.createSite') }}</el-button>
      </template>
    </CpEmpty>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import CpSectionHeader from '@/components/base/CpSectionHeader.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import type { SiteVO, FloorVO } from '@/types/space/scene'

const { t } = useI18n()
const router = useRouter()

const sites = ref<SiteVO[]>([])
const floorMap = reactive<Record<string, FloorVO[]>>({})
const floorCount = computed(() => Object.values(floorMap).reduce((n, arr) => n + arr.length, 0))

onMounted(async () => {
  const res = await siteApi.list()
  sites.value = res.data || []
  // 全站点楼层并发拉取汇总（Promise.all 取简；站点数量级小，无分页压力）
  const results = await Promise.all(sites.value.map((s) => floorApi.list(s.id!)))
  sites.value.forEach((s, i) => { floorMap[s.id!] = results[i]?.data || [] })
})

// —— 导航：standalone 页一律 named-push（路径参数）——
function gotoViewer(s: SiteVO) { router.push({ name: 'space-viewer', params: { siteId: s.id } }) }
function gotoStacked(s: SiteVO) { router.push({ name: 'space-stacked', params: { siteId: s.id } }) }
function gotoPlanning(s: SiteVO) { router.push({ path: '/space/planning', query: { siteId: s.id } }) }
function gotoEditor(f: FloorVO) { router.push({ name: 'space-editor', params: { floorId: f.id } }) }
function gotoFloorViewer(s: SiteVO, f: FloorVO) {
  router.push({ name: 'space-viewer', params: { siteId: s.id }, query: { floorId: f.id } })
}
function gotoCreateSite() { router.push('/space/site') }
</script>

<style scoped>
.space-home { padding: 16px; }

.stat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 12px;
}

.site-card {
  margin-top: 12px;
  background: var(--cp-card);
  border-radius: var(--cp-r-lg);
  box-shadow: var(--cp-shadow-1);
  overflow: hidden;
}
.site-code { margin-right: 2px; }

.floor-body { padding: 4px 20px 8px; }
.floor-empty { padding: 16px 0; text-align: center; color: var(--cp-muted); font-size: var(--cp-fs-sm); }
.floor-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 0;
  border-bottom: 1px solid var(--cp-line-soft);
}
.floor-row:last-child { border-bottom: none; }
.floor-label { font-size: var(--cp-fs-sm); color: var(--cp-ink); font-weight: 700; }
.floor-actions { display: flex; align-items: center; gap: 4px; }

/* SpaceSiteView 前车之鉴：slot/自写标签内 cp-mono 因 scoped 隔离失样，本组件自补 */
.cp-mono { font-weight: 800; color: var(--cp-brand-deep); font-size: var(--cp-fs-sm); }
</style>
