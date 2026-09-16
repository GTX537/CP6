<!--
  Space 落地页 —— 站点/楼层导航入口（Space 波2；消灭 3D 编辑器/浏览器/层叠视图三条孤儿 standalone 路由）。
  范式=WmsDashboardView 的 CpStatCard 网格 + CpSectionHeader 卡片区（不套 el-card #header 插槽，改用普通 div，
  便于单测直取——本页无表格）。数据：siteApi.list 拉全站点；有模型读取权限时优先读取 Design V1 活动版本，
  无模型则回退 floorApi.list 的 Legacy 楼层，避免把 Design V1 站点误判为无楼层。
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
            <el-button
              v-permission="'space:model:read'"
              link
              type="primary"
              size="small"
              @click="gotoStudio(s)"
            >Space Studio</el-button>
            <el-button link type="primary" size="small" @click="gotoViewer(s)">{{ t('space.home.viewer3d') }}</el-button>
            <el-button link type="primary" size="small" @click="gotoStacked(s)">{{ t('space.home.stacked') }}</el-button>
            <el-button v-if="permissionStore.has('space-control-tower:view')" link type="warning" size="small" @click="gotoTower(s)">{{ t('space.home.controlTower') }}</el-button>
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
              <el-button link type="primary" size="small" @click="gotoEditor(s, f)">{{ t('space.common.edit') }}</el-button>
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
import http from '@/api/http'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import { designProjectApi } from '@/api/space/designProject'
import type { SiteVO, FloorVO } from '@/types/space/scene'
import type {
  ISpaceModelDto,
  ISpaceSceneFloorDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { usePermissionStore } from '@/stores/permission'

const { t } = useI18n()
const router = useRouter()
const permissionStore = usePermissionStore()

type HomeFloor = FloorVO & {
  authority: 'legacy' | 'design'
  versionId?: string
  editable?: boolean
}

const sites = ref<SiteVO[]>([])
const floorMap = reactive<Record<string, HomeFloor[]>>({})
const floorCount = computed(() => Object.values(floorMap).reduce((n, arr) => n + arr.length, 0))

function asSpaceModel(value: unknown): ISpaceModelDto | null {
  if (!value || typeof value !== 'object') return null
  return typeof (value as { id?: unknown }).id === 'string'
    ? value as ISpaceModelDto
    : null
}

async function getDesignModelIfExists(siteId: string) {
  const result = await http.get<unknown, unknown>(
    `/space/design/v1/sites/${encodeURIComponent(siteId)}/model`,
    {
      // Space 首页同时承载 Legacy 与 Design V1 站点；Legacy 没有模型是正常分支，
      // 让 404 进入返回值而不是全局错误 toast，再由类型守卫折叠为 null。
      validateStatus: status => (status >= 200 && status < 300) || status === 404,
    },
  )
  return asSpaceModel(result)
}

function toDesignHomeFloor(
  floor: ISpaceSceneFloorDto,
  siteId: string,
  versionId: string,
  editable: boolean,
): HomeFloor | null {
  const floorId = floor.revision?.logicalId
  if (!floorId) return null
  return {
    id: floorId,
    siteId,
    level: floor.level ?? 0,
    floorCode: floor.floorCode ?? '',
    floorName: floor.name ?? '',
    height: floor.height ?? 0,
    underlayScale: floor.underlayScale,
    underlayOffsetX: floor.underlayOffsetX ?? 0,
    underlayOffsetY: floor.underlayOffsetY ?? 0,
    originX: 0,
    originY: 0,
    authority: 'design',
    versionId,
    editable,
  }
}

async function loadSiteFloors(site: SiteVO): Promise<HomeFloor[]> {
  const siteId = site.id!
  if (permissionStore.has('space:model:read')) {
    const model = await getDesignModelIfExists(siteId)
    const versionId = model?.activeDraftVersionId ?? model?.currentPublishedVersionId
    if (versionId) {
      const designFloors = await designProjectApi.getFloors(versionId)
      return designFloors
        .map(floor => toDesignHomeFloor(
          floor,
          siteId,
          versionId,
          versionId === model?.activeDraftVersionId,
        ))
        .filter((floor): floor is HomeFloor => floor !== null)
        .sort((left, right) =>
          left.level - right.level || left.floorCode.localeCompare(right.floorCode))
    }
  }

  const legacy = await floorApi.list(siteId)
  return (legacy.data || []).map(floor => ({ ...floor, authority: 'legacy' as const }))
}

onMounted(async () => {
  const res = await siteApi.list()
  sites.value = res.data || []
  // 全站点并发读取统一首页投影（站点数量级小，无分页压力）。
  const results = await Promise.all(sites.value.map(loadSiteFloors))
  sites.value.forEach((s, i) => { floorMap[s.id!] = results[i] || [] })
})

// —— 导航：standalone 页一律 named-push（路径参数）——
function gotoViewer(s: SiteVO) { router.push({ name: 'space-viewer', params: { siteId: s.id } }) }
function gotoStacked(s: SiteVO) { router.push({ name: 'space-stacked', params: { siteId: s.id } }) }
function gotoTower(s: SiteVO) { router.push({ name: 'space-control-tower', params: { siteId: s.id } }) }
function gotoPlanning(s: SiteVO) { router.push({ path: '/space/planning', query: { siteId: s.id } }) }
function gotoStudio(s: SiteVO) {
  router.push({ name: 'space-design-start', params: { siteId: s.id } })
}
function gotoEditor(s: SiteVO, f: HomeFloor) {
  if (f.authority === 'legacy') {
    router.push({ name: 'space-editor', params: { floorId: f.id } })
    return
  }
  if (!f.editable || !f.versionId) {
    gotoStudio(s)
    return
  }
  router.push({
    name: 'space-design-underlay',
    params: { versionId: f.versionId, floorLogicalId: f.id },
  })
}
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
