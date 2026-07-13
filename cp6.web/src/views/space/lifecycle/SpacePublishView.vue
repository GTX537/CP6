<!--
  发布中心 —— Space 波3 生命周期五区块（作用域/预检/生码/发布+采纳/停用）。
  套用 SpaceCodeRuleView 范式：CpPageShell 壳 + 站点→楼层→库区级联下拉（顶栏，三块共用）。
  关键行为：
   - 选楼层即自动 codeRuleApi.precheck(floorId, zoneId?)；库区改动亦重跑。四张 CpStatCard。
   - 生码 rebuild 先 ElMessageBox.confirm 警示；成功 toast 条数 + 自动重跑预检。
   - 发布三门（空码/重复/规则错误任一非零）禁用；409 由本页 ElMessageBox.alert 兜（拦截器不 toast）；
     其余业务错误码由 http 拦截器原样 ElMessage.error（此处 catch 仅止崩，勿重复弹）。
   - 停用小节：码前缀 locateApi.search(prefix, floorId) → 表；仅 status=1 显示「停用」，409 同上。
  CpStatCard tone 合法值＝ brand|info|warn|danger（以组件类型为准）：好态用 brand，告警用 danger，
  未落位（不挡发布）用 warn。
-->
<template>
  <CpPageShell :title="t('space.publish.title')">
    <!-- ① 作用域选择 -->
    <div class="pub-scope">
      <div class="scope-field">
        <span class="scope-lbl">{{ t('space.publish.site') }}</span>
        <el-select v-model="selSiteId" filterable clearable style="width: 240px" :placeholder="t('space.publish.selectSite')">
          <el-option v-for="s in cSites" :key="s.id" :label="`${s.siteCode}　${s.siteName}`" :value="s.id!" />
        </el-select>
      </div>
      <div class="scope-field">
        <span class="scope-lbl">{{ t('space.publish.floor') }}</span>
        <el-select v-model="selFloorId" filterable clearable style="width: 240px" :placeholder="t('space.publish.selectFloor')">
          <el-option v-for="f in cFloors" :key="f.id" :label="`${f.floorCode}　${f.floorName}`" :value="f.id" />
        </el-select>
      </div>
      <div class="scope-field">
        <span class="scope-lbl">{{ t('space.publish.zone') }}</span>
        <el-select v-model="selZoneId" filterable clearable style="width: 240px" :disabled="!selFloorId" :placeholder="t('space.publish.wholeFloorHint')">
          <el-option v-for="z in cZones" :key="z.id" :label="`${z.zoneCode}　${z.zoneName}`" :value="z.id" />
        </el-select>
      </div>
    </div>

    <CpEmpty v-if="!selFloorId" :text="t('space.publish.pickFloorFirst')" />

    <template v-else>
      <!-- ② 预检卡片 -->
      <section class="pub-sec" v-loading="precheckLoading">
        <div class="sec-head">
          <span class="sec-title">{{ t('space.publish.precheckTitle') }}</span>
          <el-button size="small" @click="runPrecheck">{{ t('space.publish.recheck') }}</el-button>
        </div>
        <div class="pub-cards">
          <CpStatCard :label="t('space.publish.emptyCode')" :value="pc.emptyCodeCount" :tone="pc.emptyCodeCount > 0 ? 'danger' : 'brand'" />
          <CpStatCard :label="t('space.publish.dupGroups')" :value="pc.duplicateGroups.length" :tone="pc.duplicateGroups.length > 0 ? 'danger' : 'brand'" />
          <CpStatCard :label="t('space.publish.ruleErrors')" :value="pc.precheckErrors.length" :tone="pc.precheckErrors.length > 0 ? 'danger' : 'brand'" />
          <CpStatCard :label="t('space.publish.unplaced')" :value="pc.unplacedDraftCount" :tone="pc.unplacedDraftCount > 0 ? 'warn' : 'brand'" :sub="t('space.publish.unplacedHint')" />
        </div>
        <!-- 重复码明细：仅有重复组时展开，逐组列出冲突库位 id 列表（后端 DuplicateGroups = List<List<Guid>>） -->
        <el-collapse v-if="pc.duplicateGroups.length" class="pub-dups">
          <el-collapse-item
            v-for="(grp, gi) in pc.duplicateGroups"
            :key="gi"
            :name="gi"
            :title="t('space.publish.dupGroupTitle', { n: gi + 1, c: grp.length })"
          >
            <ul class="dup-locs">
              <li v-for="(loc, li) in grp" :key="li" class="cp-mono">{{ loc }}</li>
            </ul>
          </el-collapse-item>
        </el-collapse>
        <ul v-if="pc.precheckErrors.length" class="pub-errs">
          <li v-for="(e, i) in pc.precheckErrors" :key="i">{{ e }}</li>
        </ul>
        <!-- 规则错误存在时引导回编码规则页修复（路由 /space/code-rule） -->
        <div v-if="pc.precheckErrors.length" class="pub-errs-link">
          <router-link :to="'/space/code-rule'" class="cp-link">{{ t('space.publish.goCodeRule') }}</router-link>
        </div>
      </section>

      <!-- ③ 生码 -->
      <section class="pub-sec">
        <div class="sec-head"><span class="sec-title">{{ t('space.publish.genTitle') }}</span></div>
        <div class="pub-row">
          <el-radio-group v-model="genMode">
            <el-radio value="fill-empty">{{ t('space.publish.mode.fillEmpty') }}</el-radio>
            <el-radio value="rebuild">{{ t('space.publish.mode.rebuild') }}</el-radio>
          </el-radio-group>
          <el-button v-permission="'space-code-rule:generate'" type="primary" :loading="genLoading" @click="onGenerate">{{ t('space.publish.genBtn') }}</el-button>
        </div>
      </section>

      <!-- ④ 发布 + 存量采纳 -->
      <section class="pub-sec">
        <div class="sec-head"><span class="sec-title">{{ t('space.publish.publishTitle') }}</span></div>
        <div class="pub-row">
          <el-button v-permission="'space-publish:publish'" type="primary" :loading="publishLoading" :disabled="!canPublish" @click="onPublish">{{ t('space.publish.doPublish') }}</el-button>
          <span v-if="!canPublish" class="pub-gate">{{ t('space.publish.gateHint') }}</span>
          <el-button v-permission="'space-publish:adopt'" @click="openAdopt">{{ t('space.publish.adoptBtn') }}</el-button>
        </div>
      </section>

      <!-- ⑤ 停用小节 -->
      <section class="pub-sec">
        <div class="sec-head"><span class="sec-title">{{ t('space.publish.deactTitle') }}</span></div>
        <div class="pub-row">
          <el-input v-model="stopPrefix" class="stop-prefix" style="width: 260px" clearable :placeholder="t('space.publish.prefixPh')" @keyup.enter="runSearch" />
          <el-button :loading="searchLoading" @click="runSearch">{{ t('space.publish.searchBtn') }}</el-button>
        </div>
        <el-table v-if="searchRows.length" :data="searchRows" size="small" border style="margin-top: 12px">
          <el-table-column :label="t('space.publish.col.code')" min-width="200">
            <template #default="{ row }"><span class="cp-mono">{{ row.locationCode || row.locationId }}</span></template>
          </el-table-column>
          <el-table-column :label="t('space.publish.col.status')" width="140">
            <template #default="{ row }">
              <CpTag :tone="statusTone(row.status)">{{ t(`space.publish.status.${row.status}`) }}</CpTag>
            </template>
          </el-table-column>
          <el-table-column :label="t('space.common.action')" width="120" align="center">
            <template #default="{ row }">
              <el-button v-if="row.status === 1" v-permission="'space-publish:deactivate'" link type="danger" size="small" @click="onDeactivate(row)">{{ t('space.publish.deactivate') }}</el-button>
            </template>
          </el-table-column>
        </el-table>
        <CpEmpty v-else-if="searched" :text="t('space.publish.noMatch')" />
      </section>
    </template>

    <!-- 存量采纳弹窗 -->
    <el-dialog v-model="adoptVisible" :title="t('space.publish.adoptTitle')" width="560">
      <div class="adopt-codes">
        <div class="adopt-hint">{{ t('space.publish.adoptHint') }}</div>
        <el-input v-model="adoptText" type="textarea" :rows="8" :placeholder="t('space.publish.adoptPh')" />
      </div>
      <div v-if="adoptResult" class="adopt-result">
        <div class="ar-imported">{{ t('space.publish.imported', { n: adoptResult.imported }) }}</div>
        <div v-if="adoptResult.skipped.length" class="ar-skipped">
          <div class="ar-skipped-lbl">{{ t('space.publish.skipped', { n: adoptResult.skipped.length }) }}</div>
          <ul>
            <li v-for="(c, i) in adoptResult.skipped" :key="i" class="cp-mono">{{ c }}</li>
          </ul>
        </div>
      </div>
      <template #footer>
        <el-button @click="adoptVisible = false">{{ t('space.common.cancel') }}</el-button>
        <el-button type="primary" :loading="adoptLoading" @click="onAdopt">{{ t('space.publish.adoptConfirm') }}</el-button>
      </template>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { codeRuleApi } from '@/api/space/codeRule'
import { publishApi } from '@/api/space/publish'
import { zoneApi } from '@/api/space/zone'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import { locateApi } from '@/api/space/locate'
import type { SiteVO, FloorVO, ZoneVO, CodePrecheckResp } from '@/types/space/scene'
import type { LocateResult } from '@/types/space/viewer'

const { t } = useI18n()

// —— ① 作用域级联 ——
const cSites = ref<SiteVO[]>([])
const cFloors = ref<FloorVO[]>([])
const cZones = ref<ZoneVO[]>([])
const selSiteId = ref<string | undefined>()
const selFloorId = ref<string | undefined>()
const selZoneId = ref<string | undefined>()

async function loadSites() {
  const res = await siteApi.list().catch(() => ({ data: [] as SiteVO[] }) as { data: SiteVO[] })
  cSites.value = res.data || []
}
loadSites()

watch(selSiteId, async (nv) => {
  cFloors.value = []
  cZones.value = []
  selFloorId.value = undefined
  selZoneId.value = undefined
  if (nv) cFloors.value = await floorApi.list(nv).then((r) => r.data || []).catch(() => [])
})
// 触发链：切楼层→floor watcher 清库区并「独家」跑一次预检（zone=undefined）；切库区→zone watcher 跑预检。
// floor watcher 重置 selZoneId 会连锁触发 zone watcher，用 suppressZoneWatch 抑制该次连锁——否则同参双发。
let suppressZoneWatch = false
watch(selFloorId, async (nv) => {
  cZones.value = []
  if (selZoneId.value !== undefined) suppressZoneWatch = true // 仅当确有变更（保证 zone watcher 必触发以复位标志）
  selZoneId.value = undefined
  // 切楼层重置停用搜索态
  searchRows.value = []
  searched.value = false
  if (nv) {
    cZones.value = await zoneApi.list(nv).then((r) => r.data || []).catch(() => [])
    await runPrecheck()
  } else {
    pcSeq++ // 作废任何在途 precheck，防其后到时回填复活已清空的结果
    precheck.value = null
  }
})
watch(selZoneId, () => {
  if (suppressZoneWatch) { suppressZoneWatch = false; return } // 来自切楼层的连锁重置，floor watcher 已跑预检
  if (selFloorId.value) runPrecheck()
})

// —— ② 预检 ——
const precheck = ref<CodePrecheckResp | null>(null)
const precheckLoading = ref(false)
const emptyPc: CodePrecheckResp = { emptyCodeCount: 0, duplicateGroups: [], precheckErrors: [], unplacedDraftCount: 0 }
const pc = computed<CodePrecheckResp>(() => precheck.value || emptyPc)

// seq 守卫乱序响应：快速切楼层/库区时，只有最新一次请求的结果生效（照 CpListPage.load 范式）
let pcSeq = 0
async function runPrecheck() {
  if (!selFloorId.value) { precheck.value = null; return }
  const id = ++pcSeq
  precheckLoading.value = true
  try {
    const res = await codeRuleApi.precheck(selFloorId.value, selZoneId.value || undefined)
    if (id !== pcSeq) return // 已有更新的请求发出，丢弃本次过期响应
    precheck.value = res.data
  } catch {
    if (id !== pcSeq) return
    precheck.value = null // 错误 message 已由 http 拦截器提示
  } finally {
    if (id === pcSeq) precheckLoading.value = false
  }
}

// —— ③ 生码 ——
const genMode = ref<'fill-empty' | 'rebuild'>('fill-empty')
const genLoading = ref(false)
async function onGenerate() {
  if (!selFloorId.value) return
  if (genMode.value === 'rebuild') {
    try {
      await ElMessageBox.confirm(t('space.publish.rebuildWarn'), t('space.common.confirm'), { type: 'warning' })
    } catch { return } // 取消
  }
  const seq = pcSeq // 生码前快照：期间若切楼层/库区（watcher bump pcSeq），本次结果作废
  genLoading.value = true
  try {
    const res = await codeRuleApi.generate(selFloorId.value, genMode.value, selZoneId.value || undefined)
    if (seq !== pcSeq) return // 作用域已变，丢弃过期的成功提示与重检
    ElMessage.success(t('space.publish.genDone', { n: (res.data || []).length }))
    await runPrecheck()
  } catch {
    /* 拦截器已提示 */
  } finally {
    genLoading.value = false
  }
}

// —— ④ 发布 + 采纳 ——
const publishLoading = ref(false)
// 三门：空码/重复/规则错误任一非零则禁用；未落位不挡
const canPublish = computed(() =>
  !!precheck.value &&
  pc.value.emptyCodeCount === 0 &&
  pc.value.duplicateGroups.length === 0 &&
  pc.value.precheckErrors.length === 0,
)

async function onPublish() {
  if (!selFloorId.value || !canPublish.value) return
  publishLoading.value = true
  try {
    const res = await publishApi.publishFloor(selFloorId.value, selZoneId.value || undefined)
    ElMessage.success(t('space.publish.publishDone', { n: res.data?.published ?? 0 }))
    await runPrecheck()
  } catch (e) {
    if ((e as { response?: { status?: number } })?.response?.status === 409) {
      ElMessageBox.alert(t('space.publish.conflict409'))
    }
    // 其余错误拦截器已 toast
  } finally {
    publishLoading.value = false
  }
}

const adoptVisible = ref(false)
const adoptText = ref('')
const adoptLoading = ref(false)
const adoptResult = ref<{ imported: number; skipped: string[] } | null>(null)
function openAdopt() {
  adoptText.value = ''
  adoptResult.value = null
  adoptVisible.value = true
}
async function onAdopt() {
  const codes = adoptText.value.split('\n').map((s) => s.trim()).filter(Boolean)
  if (!codes.length) { ElMessage.warning(t('space.publish.adoptEmpty')); return }
  adoptLoading.value = true
  try {
    const res = await publishApi.adopt(codes.map((code) => ({ code })))
    adoptResult.value = res.data
    await runPrecheck() // 采纳新增已发布库位会使预检过期，重跑（runPrecheck 内部已守卫无楼层场景）
  } catch (e) {
    if ((e as { response?: { status?: number } })?.response?.status === 409) {
      ElMessageBox.alert(t('space.publish.conflict409'))
    }
  } finally {
    adoptLoading.value = false
  }
}

// —— ⑤ 停用 ——
const stopPrefix = ref('')
const searchLoading = ref(false)
const searched = ref(false)
const searchRows = ref<LocateResult[]>([])
function statusTone(s: number): Tone {
  return s === 1 ? 'ok' : s === 2 ? 'danger' : 'muted'
}
async function runSearch() {
  const prefix = stopPrefix.value.trim()
  if (!prefix) return
  searchLoading.value = true
  try {
    const res = await locateApi.search(prefix, selFloorId.value)
    searchRows.value = res.data || []
    searched.value = true
  } catch {
    searchRows.value = []
    searched.value = true
  } finally {
    searchLoading.value = false
  }
}
async function onDeactivate(row: LocateResult) {
  try {
    await ElMessageBox.confirm(
      t('space.publish.deactivateWarn', { code: row.locationCode || row.locationId }),
      t('space.common.confirm'),
      { type: 'warning' },
    )
  } catch { return } // 取消
  try {
    await publishApi.deactivate(row.locationId)
    ElMessage.success(t('space.common.success'))
    await runSearch()
  } catch (e) {
    if ((e as { response?: { status?: number } })?.response?.status === 409) {
      ElMessageBox.alert(t('space.publish.conflict409'))
    }
    // 其余业务错误码由拦截器原样提示
  }
}
</script>

<style scoped>
.pub-scope { display: flex; flex-wrap: wrap; gap: 20px; align-items: center;
  background: var(--cp-card); border-radius: var(--cp-r-lg); box-shadow: var(--cp-shadow-1); padding: 16px 20px; }
.scope-field { display: flex; align-items: center; gap: 10px; }
.scope-lbl { font-size: var(--cp-fs-xs); font-weight: 800; color: var(--cp-muted); letter-spacing: .4px; }

.pub-sec { background: var(--cp-card); border-radius: var(--cp-r-lg); box-shadow: var(--cp-shadow-1); padding: 16px 20px; }
.sec-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 14px; }
.sec-title { font-size: var(--cp-fs-base); font-weight: 800; color: var(--cp-ink); }
.pub-row { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
.pub-gate { font-size: var(--cp-fs-sm); color: var(--cp-danger); font-weight: 700; }

.pub-cards { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; }
.pub-errs { margin: 14px 0 0; padding-left: 18px; color: #cf1322; font-size: var(--cp-fs-sm); }
.pub-errs li { margin: 2px 0; }

.pub-dups { margin-top: 14px; }
.dup-locs { margin: 0; padding-left: 18px; font-size: var(--cp-fs-sm); }
.dup-locs li { margin: 2px 0; }

.pub-errs-link { margin-top: 10px; font-size: var(--cp-fs-sm); }
.cp-link { color: var(--cp-brand-deep); font-weight: 700; text-decoration: underline; cursor: pointer; }
.cp-link:hover { color: var(--cp-brand); }

.adopt-codes { display: flex; flex-direction: column; gap: 8px; }
.adopt-hint { font-size: var(--cp-fs-sm); color: var(--cp-muted); }
.adopt-result { margin-top: 14px; font-size: var(--cp-fs-sm); }
.ar-imported { color: #389e0d; font-weight: 700; }
.ar-skipped { margin-top: 8px; }
.ar-skipped-lbl { color: var(--cp-warn); font-weight: 700; margin-bottom: 4px; }
.ar-skipped ul { margin: 0; padding-left: 18px; }

/* 本地补 .cp-mono（scoped；波2 前车之鉴——勿依赖模板全局类） */
.cp-mono { font-family: var(--cp-font-mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace);
  font-weight: 700; color: var(--cp-brand-deep); }
</style>
