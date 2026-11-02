<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { identityStamp, requireIdentity } from '@/auth/identity'
import { personalFormApi as api, type PersonalCatalogItem } from '@/api/oa/personalForm'
const router = useRouter(), items = ref<PersonalCatalogItem[]>([]), page = ref(1), total = ref(0), pageSize = ref(20), search = ref('')
const busy = ref(false), error = ref('')
let controller: AbortController | undefined, generation = 0, live = true
async function load(reset = false) {
  if (reset) page.value = 1
  controller?.abort(); controller = new AbortController()
  const request = ++generation, identity = identityStamp()
  busy.value = true; error.value = ''; items.value = []; total.value = 0
  try {
    const value = await api.catalog(page.value, search.value, controller.signal)
    requireIdentity(identity)
    if (!live || request !== generation) return
    items.value = value.items; total.value = value.total; pageSize.value = value.pageSize
  } catch (e: any) {
    if (live && request === generation && identity === identityStamp()) error.value = [401, 403].includes(e?.response?.status) ? '当前无权读取表单目录，请确认登录与使用权限。' : '无法读取表单目录，请重新读取。'
  } finally { if (live && request === generation) busy.value = false }
}
function open(item: PersonalCatalogItem) { void router.push({ path: '/oa/form-initiate', query: { formKey: item.formKey, definition: 'published' } }) }
onMounted(() => void load())
onBeforeUnmount(() => { live = false; generation++; controller?.abort() })
</script>
<template>
  <section aria-label="已发布表单目录">
    <p>选择表单开始填写。提交时会再次核对当前启用版本和使用权限。</p>
    <form @submit.prevent="load(true)"><label>搜索表单<input v-model="search" maxlength="200" /></label><button :disabled="busy">搜索</button><button type="button" :disabled="busy" @click="load()">重新读取</button></form>
    <p v-if="error" role="alert">{{ error }}</p><p v-else-if="busy" role="status">正在读取表单</p>
    <p v-else-if="!items.length">暂无可使用的已发布表单</p>
    <div class="cards"><article v-for="item in items" :key="item.formKey"><h3>{{ item.name }}</h3><p>{{ item.category ?? '未分类' }} · {{ item.subCategory ?? '其他' }} · v{{ item.formVersion }}</p><button @click="open(item)">填写 {{ item.name }}</button></article></div>
    <nav v-if="total"><button :disabled="busy || page === 1" @click="page--; load()">上一页</button><span>{{ page }} · 共 {{ total }} 项</span><button :disabled="busy || page * pageSize >= total" @click="page++; load()">下一页</button></nav>
  </section>
</template>
<style scoped>
form, nav { display: flex; gap: 12px; align-items: center; margin: 16px 0; } label { display: flex; gap: 8px; align-items: center; } input, button { padding: 8px 12px; border: 1px solid var(--el-border-color); border-radius: 6px; background: var(--el-bg-color); color: inherit; } button { cursor: pointer; } button:disabled { opacity: .5; cursor: default; } .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 16px; } article { border: 1px solid var(--el-border-color); border-radius: 8px; padding: 16px; } h3 { margin-top: 0; } [role=alert] { color: var(--el-color-danger); }
</style>
