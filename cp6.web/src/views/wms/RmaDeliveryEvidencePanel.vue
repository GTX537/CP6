<template>
  <section aria-label="客户签收证据">
    <h2>客户签收证据</h2><p>记录本发运行的签收单据及数量区间，供退货核定参考。证据登记本身不代表已批准退货。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>原签收证据请求待确认。</p><button :disabled="busy" @click="recover(false)">查询原证据请求</button><button :disabled="busy" @click="recover(true)">查询并重派原证据请求</button></div>
    <button :disabled="busy" @click="reload">刷新签收证据</button>
    <p v-if="loaded && !items.length">本发运行尚无签收证据。</p>
    <article v-for="item in items" :key="item.evidence.reference.id">
      <h3>{{ item.evidence.documentReference }}</h3><p>{{ item.evidence.occurredAt }} · 记载数量 {{ item.evidence.reportedQuantity.value }} {{ item.evidence.reportedQuantity.uom.code }}</p>
      <p>数量区间：{{ item.evidence.ranges.map(x => `[${x.from}, ${x.to})`).join('；') }}</p>
      <p v-if="item.evidence.notes">{{ item.evidence.notes }}</p><p>登记于 {{ item.evidence.recordedAt }}</p>
      <button v-for="(file, index) in item.evidence.fileRefs" :key="file.id" :disabled="busy" @click="download(file, index)">下载签收附件 {{ index + 1 }}</button>
      <p v-if="item.withdrawal">已撤回：{{ item.withdrawal.reason }} · {{ item.withdrawal.recordedAt }}</p>
      <form v-else-if="canWithdraw" @submit.prevent="withdraw(item)"><fieldset :disabled="busy || !!pending || storageError"><label>撤回原因<input v-model="withdrawReasons[item.evidence.reference.id]" :aria-label="`撤回原因${item.evidence.reference.id}`" required maxlength="500" /></label><button type="submit">撤回证据 {{ item.evidence.documentReference }}</button></fieldset></form>
    </article>
    <nav aria-label="证据分页"><button :disabled="busy || page === 1" @click="move(-1)">上一页证据</button><button :disabled="busy || !hasMore" @click="move(1)">下一页证据</button></nav>
    <details v-if="canRecord"><summary>登记签收证据</summary>
      <form @submit.prevent="record"><fieldset :disabled="busy || !!pending || storageError">
        <label>签收单据编号<input v-model="documentReference" aria-label="签收单据编号" required maxlength="200" /></label>
        <label>签收时间（含时区）<input v-model="occurredAt" aria-label="签收时间" placeholder="2026-10-29T09:00:00+08:00" required /></label>
        <p>按原发运数量位置填写左闭右开区间，例如 [0, 2.5)。多个证据可能描述同一批货，数量不重复累计。</p>
        <div v-for="(range, index) in ranges" :key="index"><label>起点<input v-model="range.from" :aria-label="`签收区间起点${index + 1}`" inputmode="decimal" required /></label><label>终点<input v-model="range.to" :aria-label="`签收区间终点${index + 1}`" inputmode="decimal" required /></label><button type="button" :disabled="ranges.length === 1" @click="ranges.splice(index, 1)">移除区间</button></div>
        <button type="button" :disabled="ranges.length >= 100" @click="ranges.push({ from: '', to: '' })">添加区间</button>
        <label>查找已上传附件<input v-model="fileKeyword" aria-label="附件名称" maxlength="200" /></label><button type="button" @click="searchFiles">查找附件</button>
        <label v-for="file in fileOptions" :key="file.reference.id"><input type="checkbox" :checked="selectedFiles.some(x => x.id === file.reference.id)" :disabled="file.availability !== 'AVAILABLE'" @change="chooseFile(file, ($event.target as HTMLInputElement).checked)" />{{ file.name }} · {{ file.availability }}</label>
        <p>已选 {{ selectedFiles.length }} 个附件</p><button type="button" :disabled="filePage === 1" @click="moveFiles(-1)">上一页附件</button><button type="button" :disabled="!fileMore" @click="moveFiles(1)">下一页附件</button>
        <label>备注<textarea v-model="notes" aria-label="签收备注" maxlength="1000" /></label><button type="submit">保存签收证据</button>
      </fieldset></form>
    </details>
  </section>
</template>
<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef, toRaw, watch } from 'vue'
import type { RmaOriginal, RmaRef } from '@/api/wms/rmaFormalApplications'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { rmaDeliveryEvidenceApi as api, type DeliveryEvidenceFile, type DeliveryEvidencePending, type DeliveryEvidenceView } from '@/api/wms/rmaDeliveryEvidence'
import { pendingDeliveryEvidence, recoverDeliveryEvidence, writeDeliveryEvidence } from '@/auth/rmaDeliveryEvidenceRecovery'
const props = defineProps<{ scope: RmaScope; option: RmaRef; original: RmaOriginal; canRecord: boolean; canWithdraw: boolean }>()
const items = shallowRef<DeliveryEvidenceView[]>([]), fileOptions = shallowRef<DeliveryEvidenceFile[]>([]), selectedFiles = shallowRef<RmaRef[]>([]), pending = shallowRef<DeliveryEvidencePending | null>(null)
const busy = ref(false), loaded = ref(false), storageError = ref(false), error = ref(''), notice = ref(''), page = ref(1), hasMore = ref(false), filePage = ref(1), fileMore = ref(false)
const documentReference = ref(''), occurredAt = ref(''), ranges = ref([{ from: '', to: '' }]), notes = ref(''), fileKeyword = ref(''), searchedFiles = ref(''), withdrawReasons = ref<Record<string, string>>({})
let epoch = 0, live = true
const downloads = new Set<string>()
function clear() { items.value = []; loaded.value = false; fileOptions.value = []; selectedFiles.value = []; documentReference.value = ''; occurredAt.value = ''; ranges.value = [{ from: '', to: '' }]; notes.value = ''; fileKeyword.value = ''; searchedFiles.value = ''; withdrawReasons.value = {}; hasMore.value = false; fileMore.value = false; page.value = 1; filePage.value = 1 }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const version = epoch, check = () => { if (!live || version !== epoch) throw new Error('页面范围已变化。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { await action(check) } catch (e: any) { if (live && version === epoch) { if ([401, 403].includes(e?.response?.status)) clear(); error.value = e?.response?.data?.detail || e.message || '签收证据请求未完成。' } }
  finally { if (live && version === epoch) {
    try { const value = await pendingDeliveryEvidence(props.scope); check(); pending.value = value; storageError.value = false } catch (e: any) { if (live && version === epoch) { storageError.value = true; error.value = e.message } }
    if (live && version === epoch) busy.value = false
  } }
}
async function load(check: () => void) { const value = await api.search(props.option, props.original, page.value); check(); items.value = value.items; hasMore.value = value.hasMore; loaded.value = true }
async function reload() { await run(async check => { items.value = []; loaded.value = false; await load(check) }) }
async function download(file: RmaRef, index: number) { await run(async check => {
  const blob = await api.download(file); check()
  const url = URL.createObjectURL(new Blob([blob], { type: 'application/octet-stream' })); downloads.add(url)
  const link = document.createElement('a'); link.href = url; link.download = `签收附件-${index + 1}`; link.click()
  window.setTimeout(() => { URL.revokeObjectURL(url); downloads.delete(url) }, 1000)
}) }
async function move(delta: number) { await run(async check => { page.value += delta; items.value = []; await load(check) }) }
async function loadFiles(check: () => void) { const value = await api.files(props.option, searchedFiles.value || null, filePage.value); check(); fileOptions.value = value.items; fileMore.value = value.hasMore }
async function searchFiles() { await run(async check => { filePage.value = 1; searchedFiles.value = fileKeyword.value; fileOptions.value = []; await loadFiles(check) }) }
async function moveFiles(delta: number) { await run(async check => { filePage.value += delta; fileOptions.value = []; await loadFiles(check) }) }
function chooseFile(file: DeliveryEvidenceFile, checked: boolean) { selectedFiles.value = selectedFiles.value.filter(x => x.id !== file.reference.id); if (checked) selectedFiles.value = [...selectedFiles.value, file.reference] }
async function record() { await run(async check => {
  if (!documentReference.value.trim() || !/(Z|[+-]\d{2}:\d{2})$/.test(occurredAt.value) || !selectedFiles.value.length || ranges.value.some(x => !x.from || !x.to)) throw new Error('请填写单据编号、含时区的时间、数量区间并选择附件。')
  const body = { scopeOptionRef: toRaw(props.option), shipmentRef: toRaw(props.original.shipmentRef), shipmentLineId: props.original.shipmentLineId, evidenceId: crypto.randomUUID(), documentReference: documentReference.value, occurredAt: occurredAt.value, ranges: ranges.value.map(x => ({ from: x.from, to: x.to })), fileRefs: selectedFiles.value.map(x => toRaw(x)), notes: notes.value || null }
  await writeDeliveryEvidence(props.scope, { version: 1, commandId: crypto.randomUUID(), kind: 'RECORD', body }, check); check(); clear(); await load(check); notice.value = '签收证据已登记。'
}) }
async function withdraw(item: DeliveryEvidenceView) { await run(async check => {
  const reason = withdrawReasons.value[item.evidence.reference.id]; if (!reason?.trim()) throw new Error('请填写撤回原因。')
  await writeDeliveryEvidence(props.scope, { version: 1, commandId: crypto.randomUUID(), kind: 'WITHDRAW', body: { scopeOptionRef: toRaw(props.option), evidenceRef: item.evidence.reference, reason } }, check); check(); await load(check); notice.value = '证据已撤回，原件和撤回原因保留。'
}) }
async function recover(retry: boolean) { await run(async check => { const result = await recoverDeliveryEvidence(props.scope, retry, check); check(); if (result) { await load(check); notice.value = `已找回原证据请求：${result.evidence.documentReference}。` } else notice.value = '尚未观察到原结果，原请求保持待确认。' }) }
watch(() => [props.scope.tenantId, props.scope.actorId, props.option.id, props.option.digest, props.original.shipmentRef.digest, props.original.shipmentLineId], () => { epoch++; busy.value = false; pending.value = null; clear(); void reload() }, { immediate: true })
onBeforeUnmount(() => { live = false; epoch++; for (const url of downloads) URL.revokeObjectURL(url); downloads.clear() })
</script>
