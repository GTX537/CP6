<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { userApi } from '@/api/sys/user'
import { identityStamp, requireIdentity } from '@/auth/identity'
defineProps<{ label: string }>()
const model = defineModel<string>(), options = ref<{ id: string; name: string }[]>([]), busy = ref(false), error = ref('')
let generation = 0, live = true
async function search(keyword: string) {
  const current = ++generation, identity = identityStamp(); options.value = []; error.value = ''
  if (!keyword) { busy.value = false; return }
  busy.value = true
  try {
    const value: any = await userApi.getList({ page: 1, pageSize: 20, keyword }); requireIdentity(identity)
    if (live && current === generation) options.value = value.rows.map((u: any) => ({ id: u.id, name: u.nickName || u.userName }))
  } catch { if (live && current === generation && identity === identityStamp()) error.value = '无法搜索人员，请检查读取权限后重试。' }
  finally { if (live && current === generation) busy.value = false }
}
onBeforeUnmount(() => { live = false; generation++ })
</script>
<template><label>{{ label }}<el-select v-model="model" :aria-label="label" filterable remote clearable :remote-method="search" :loading="busy" placeholder="搜索人员"><el-option v-for="user in options" :key="user.id" :value="user.id" :label="user.name" /></el-select></label><p v-if="error" role="alert">{{ error }}</p></template>
