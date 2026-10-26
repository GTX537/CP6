<template>
  <main class="tool-sources" :aria-busy="busy">
    <header><div><h1>版型工具原来源</h1><p>按完整版型身份核对原资料及观察历史。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p>
    <section class="panel">
      <form @submit.prevent="loadPage(1)"><label>基地、完整编号或名称 <input v-model="search" aria-label="工具来源搜索" maxlength="100" /></label><button :disabled="busy">搜索</button></form>
      <div class="table-scroll"><table><thead><tr><th>基地 / 完整编号</th><th>名称</th><th>业务修订</th><th>观察版本</th><th>操作</th></tr></thead><tbody>
        <tr v-for="row in rows" :key="row.reference.id"><td>{{ row.source.identity.baseCode }} / {{ row.source.identity.number }}</td><td>{{ row.source.identity.name }}</td><td>{{ row.source.identity.businessRevision }}</td><td>{{ row.reference.version }}</td><td><button :disabled="busy" @click="open(row)">查看来源</button></td></tr>
      </tbody></table></div><p v-if="!rows.length">当前范围内没有版型来源。</p>
      <nav aria-label="工具来源分页"><button :disabled="busy || page === 1" @click="loadPage(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadPage(page + 1)">下一页</button></nav>
    </section>
    <section v-if="selected" class="panel" aria-label="当前工具来源">
      <h2>{{ selected.source.identity.name }}</h2><p class="identity">{{ selected.source.identity.baseCode }} / {{ selected.source.identity.number }} / 业务修订 {{ selected.source.identity.businessRevision }}</p>
      <p>当前观察版本 {{ selected.reference.version }}；记录时间 {{ selected.observedAt }}。</p>
      <p>技术批准：未知；实物资格：未知。旧传输状态与附件勾选不能作为批准或文件证明。</p>
      <p>原适用期：{{ selected.source.validity.startDate }} 至 {{ selected.source.validity.endDate }}；{{ selected.source.control.isDeleted ? '原记录已删除' : '原记录存在' }}。</p>
      <p>旧传输状态：{{ selected.source.control.legacyTransferStatus }}；旧 mc 转送标志：{{ selected.source.control.legacyMcTransferred ? '是' : '否' }}。</p>
      <h3>原技术资料</h3><dl><template v-for="(value, key) in selected.source.technical" :key="key"><dt>{{ labels[key] || key }}</dt><dd>{{ display(value) }}</dd></template></dl>
      <details><summary>旧附件标志与所需资料</summary><h3>原附件勾选</h3><dl><template v-for="(value, key) in selected.source.legacyAttachmentFlags" :key="key"><dt>{{ labels[key] || key }}</dt><dd>{{ display(value) }}</dd></template></dl><h3>原所需资料</h3><dl><template v-for="(value, key) in selected.source.requirements" :key="key"><dt>{{ labels[key] || key }}</dt><dd>{{ display(value) }}</dd></template></dl></details>
      <form @submit.prevent="loadHistory"><label>观察历史版本 <input v-model="historyVersion" aria-label="工具观察历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取历史</button></form>
      <p>观察历史从首次记录开始。业务修订号不代表此前已有对应的观察原件。</p>
      <article v-if="history" aria-label="工具观察历史"><h3>观察版本 {{ history.reference.version }} · {{ history.isCurrent ? '当前观察' : '历史观察' }}</h3>
        <p>{{ history.source.identity.baseCode }} / {{ history.source.identity.number }} / 业务修订 {{ history.source.identity.businessRevision }}；{{ history.observedAt }}</p><pre>{{ history.originalJson }}</pre></article>
      <details><summary>来源原件与核对信息</summary><p class="identity">原对象：{{ selected.reference.id }}；摘要：{{ selected.reference.digest }}</p><p class="identity">观察时原生令牌：{{ selected.observedNativeRowVersion }}；当前原生令牌：{{ selected.currentNativeRowVersion }}</p><pre>{{ selected.originalJson }}</pre></details>
    </section>
  </main>
</template>
<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { plateSourceApi, type PlateSourceEnvelope, type PlateSourceVersion } from '@/api/erp/plateMoldSources'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
const busy = ref(false), error = ref(''), search = ref(''), page = ref(1), historyVersion = ref('1')
const rows = ref<PlateSourceVersion[]>([]), selected = ref<PlateSourceVersion | null>(null), history = ref<PlateSourceVersion | null>(null)
let generation = 0, live = true
const labels: Record<string, string> = {
  representativeProductCode: '原代表产品编码', newVersionCode: '原新版区分', processCode: '原工程编码', typeClass: '版型分类', endPlaceCode: '原终止位置编码', sheetFlute: '纸板楞型',
  paperCodeF: '表层纸编码', printCodeF: '表层印刷编码', embossCodeF: '表层压纹编码', makerCodeF: '表层制造商编码',
  paperCodeC: '中层纸编码', printCodeC: '中层印刷编码', embossCodeC: '中层压纹编码', makerCodeC: '中层制造商编码',
  paperCodeB: '底层纸编码', printCodeB: '底层印刷编码', embossCodeB: '底层压纹编码', makerCodeB: '底层制造商编码',
  extractionDirection: '抽取方向', duplicatePlate: '复版标志', sheetWidth: '原纸宽', sheetFlow: '原纸流向长度', bladeWidth: '刀宽', bladeFlow: '刀流向长度',
  compositionQuantity: '面付数', manufacturingQuantity: '个数', colorQuantity: '色数', bookQuantity: '本数', flexoThickness: '柔版厚度', cylinderSize: '圆筒尺寸',
  strippingCode: '清废编码', standingCode: '立版编码', hammerCode: '锤击编码', oversightCode: '原检查编码',
  sheetFront: '纸板正面', sheetBack: '纸板背面', actual: '实物', baseplate: '底版', positive: '阳片', negative: '阴片', mo: 'MO', fd: 'FD',
  draft: '原稿', mylar: '聚酯片', galley: '校样', proof: '打样', blueprint: '蓝图', comp: '组合稿', designSheet: '设计单', otherName: '其他资料名称', other: '其他资料',
}
function display(value: string | number | boolean | null) { return value == null ? '未填写' : typeof value === 'boolean' ? value ? '是' : '否' : value }
function response<T>(value: PlateSourceEnvelope<T>): T { if (value.code !== 0) throw new Error('来源读取未成功。'); return value.data }
function clearPrivate() { rows.value = []; selected.value = null; history.value = null; search.value = ''; page.value = 1; historyVersion.value = '1' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), current = generation
  const check = () => { requireIdentity(stamp); if (!live || current !== generation) throw new Error('页面已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() }
  catch (failure: any) { try { check(); if ([401, 403].includes(failure?.response?.status)) clearPrivate()
    error.value = failure?.response?.status === 404 ? '没有该观察版本；此前的历史不会自动补建。' : [401, 403].includes(failure?.response?.status) ? '当前无权读取该来源，请核对权限。' : '原来源暂不可用，请刷新后核对。'
  } catch { /* Ignore obsolete identity responses. */ } }
  finally { try { check(); busy.value = false } catch { /* Identity handler owns the new page. */ } }
}
async function reload() { await run(async check => { rows.value = []; selected.value = null; history.value = null
  response(await plateSourceApi.scope()); check(); const result = response(await plateSourceApi.list(search.value, page.value)); check(); rows.value = result
}) }
async function loadPage(target: number) { await run(async check => { selected.value = null; history.value = null; rows.value = []
  const result = response(await plateSourceApi.list(search.value, target)); check(); rows.value = result; page.value = target
}) }
async function open(row: PlateSourceVersion) { await run(async check => { selected.value = null; history.value = null; historyVersion.value = '1'
  const result = response(await plateSourceApi.read(row.reference.id)); check(); selected.value = result
}) }
async function loadHistory() { const current = selected.value; if (!current) return; await run(async check => { history.value = null
  const result = response(await plateSourceApi.read(current.reference.id, historyVersion.value)); check(); history.value = result
}) }
function identityChanged() { generation++; clearPrivate(); busy.value = false; error.value = ''; if (authenticationCanRead()) void reload() }
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged); clearPrivate() })
</script>
<style scoped>
.tool-sources{max-width:1200px;margin:auto;padding:24px;color:var(--text,#18343d)}header,nav,form{display:flex;gap:14px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}.panel{background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px;padding:22px;margin-top:20px}button,input{font:inherit;padding:8px 12px;border:1px solid #bdcdd3;border-radius:6px;color:inherit;background:#fff}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}button:focus-visible,input:focus{outline:2px solid #14969e;outline-offset:2px}.table-scroll{overflow:auto}table{width:100%;min-width:680px;border-collapse:collapse}th,td{text-align:left;padding:14px;border-bottom:1px solid #dce5e9;overflow-wrap:anywhere}nav,details,form{margin-top:16px}dl{display:grid;grid-template-columns:minmax(140px,1fr) 2fr;gap:10px}dd{margin:0;overflow-wrap:anywhere}.identity,pre{overflow-wrap:anywhere;white-space:pre-wrap}pre{background:#f3f6f7;padding:14px;max-height:360px;overflow:auto}[role=alert]{color:#a02828}@media(max-width:600px){.tool-sources{padding:10px}.panel{padding:14px}}
</style>
