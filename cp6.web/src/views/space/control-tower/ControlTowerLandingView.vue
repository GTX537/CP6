<template>
  <div class="tower-landing">
    <header class="landing-hero">
      <div>
        <p>CP6 SPACE · CONTROL TOWER</p>
        <h1>{{ t('space.controlTower.title') }}</h1>
        <span>{{ t('space.controlTower.subtitle') }}</span>
      </div>
      <el-button :loading="loading" @click="loadSites">{{ t('space.controlTower.refresh') }}</el-button>
    </header>

    <div v-if="!permissionStore.has('space-control-tower:view')" class="landing-state">
      {{ t('space.controlTower.noPermission') }}
    </div>
    <div v-else-if="loading" class="site-grid">
      <el-skeleton v-for="n in 3" :key="n" animated :rows="3" class="site-card skeleton" />
    </div>
    <div v-else-if="errorMessage" class="landing-state error">
      <span>{{ errorMessage }}</span>
      <el-button type="primary" @click="loadSites">{{ t('space.controlTower.retry') }}</el-button>
    </div>
    <div v-else-if="sites.length" class="site-grid">
      <article v-for="site in sites" :key="site.id" class="site-card">
        <div class="site-code">{{ site.siteCode }}</div>
        <h2>{{ site.siteName }}</h2>
        <p>{{ site.address || site.warehouseCd || '—' }}</p>
        <el-button type="primary" @click="openTower(site)">
          {{ t('space.controlTower.open') }} →
        </el-button>
      </article>
    </div>
    <div v-else class="landing-state">{{ t('space.controlTower.empty') }}</div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { siteApi } from '@/api/space/site'
import { usePermissionStore } from '@/stores/permission'
import type { SiteVO } from '@/types/space/scene'

const { t } = useI18n()
const router = useRouter()
const permissionStore = usePermissionStore()
const sites = ref<SiteVO[]>([])
const loading = ref(false)
const errorMessage = ref('')

async function loadSites() {
  if (!permissionStore.loaded) await permissionStore.loadMyActions()
  if (!permissionStore.has('space-control-tower:view')) return
  loading.value = true
  errorMessage.value = ''
  try {
    const response = await siteApi.list()
    sites.value = response.data || []
  } catch {
    errorMessage.value = t('space.controlTower.loadFailed')
  } finally {
    loading.value = false
  }
}

function openTower(site: SiteVO) {
  router.push({ name: 'space-control-tower', params: { siteId: site.id } })
}

onMounted(loadSites)
</script>

<style scoped>
.tower-landing { min-height: 100%; padding: 24px; background: linear-gradient(145deg, #f7fbff, #eef4f8); }
.landing-hero { display: flex; justify-content: space-between; align-items: flex-end; gap: 24px; padding: 28px; color: #e6f7ff; background: radial-gradient(circle at 85% 15%, #155e75, #0f2535 58%, #091721); border-radius: 18px; box-shadow: 0 16px 40px rgb(9 23 33 / 18%); }
.landing-hero p { margin: 0 0 8px; color: #67e8f9; font-size: 12px; font-weight: 800; letter-spacing: .18em; }
.landing-hero h1 { margin: 0 0 8px; font-size: 30px; }
.landing-hero span { color: #a9c5d3; }
.site-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 16px; margin-top: 20px; }
.site-card { min-height: 180px; padding: 22px; background: #fff; border: 1px solid #dce8ee; border-radius: 14px; box-shadow: 0 8px 24px rgb(15 37 53 / 7%); }
.site-card h2 { margin: 8px 0; color: #102f42; }
.site-card p { min-height: 42px; margin: 0 0 18px; color: #657f8e; }
.site-code { color: #0891b2; font-family: ui-monospace, monospace; font-size: 12px; font-weight: 800; letter-spacing: .08em; }
.skeleton { box-sizing: border-box; }
.landing-state { display: grid; place-items: center; gap: 14px; min-height: 260px; color: #657f8e; }
.landing-state.error { color: #b42318; }
@media (max-width: 680px) { .tower-landing { padding: 12px; } .landing-hero { align-items: flex-start; flex-direction: column; } }
</style>
