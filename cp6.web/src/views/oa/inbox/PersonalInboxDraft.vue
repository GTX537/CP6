<script setup lang="ts">
import { personalFormError } from '../catalog/personalFormError'
import { onMounted, onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { personalFormApi as api, type PersonalDraftItem } from '@/api/oa/personalForm'
import PersonalFormEditor from '../catalog/PersonalFormEditor.vue'
const rows = ref<PersonalDraftItem[]>([]), total = ref(0), page = ref(1), loading = ref(false), error = ref(''), selected = ref(''), editorLocked = ref(false)
let live = true, generation = 0, stamp = identityStamp()
async function load() {
  const identity = identityStamp(), current = ++generation; loading.value = true; error.value = ''
  try { const result = await api.list(page.value); requireIdentity(identity); if (!live || current !== generation) return; rows.value = result.items; total.value = result.total }
  catch (e: any) { if (live && current === generation && identity === identityStamp()) { rows.value = []; total.value = 0; error.value = personalFormError(e) } }
  finally { if (live && current === generation) loading.value = false }
}
function changed() { if (stamp === identityStamp()) return; stamp = identityStamp(); generation++; rows.value = []; total.value = 0; selected.value = ''; loading.value = false; error.value = '身份已改变，请重新读取。' }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
onMounted(load)
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <div>
    <div class="toolbar"><span>共 {{ total }} 条本人未提交草稿</span><el-button :loading="loading" @click="load">刷新</el-button></div>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-table :data="rows" v-loading="loading" border>
      <el-table-column prop="title" label="标题" /><el-table-column prop="name" label="表单" />
      <el-table-column label="版本"><template #default="{ row }">v{{ row.formVersion }}<span v-if="row.stale"> · 有新版</span></template></el-table-column>
      <el-table-column prop="updatedAtUtc" label="更新时间" />
      <el-table-column label="操作"><template #default="{ row }"><el-button :disabled="editorLocked" @click="selected = row.draftId">查看 / 编辑</el-button></template></el-table-column>
    </el-table>
    <el-pagination v-model:current-page="page" :total="total" :page-size="20" layout="prev, pager, next" @current-change="load" />
    <section v-if="selected"><PersonalFormEditor :draft-id="selected" @changed="load" @locked="editorLocked = $event" /></section>
  </div>
</template>
<style scoped>.toolbar { display: flex; gap: 16px; align-items: center; margin-bottom: 12px; } section { margin-top: 24px; }</style>
