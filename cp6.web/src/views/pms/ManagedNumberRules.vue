<template>
  <section>
    <form class="rule-search" @submit.prevent="search">
      <el-input v-model="keyword" clearable :aria-label="t('编号域业务键')" :placeholder="t('编号域业务键')" />
      <el-button native-type="submit">{{ t('搜索') }}</el-button>
    </form>
    <el-table :data="rows" v-loading="busy">
      <el-table-column prop="bizKey" :label="t('业务键')" />
      <el-table-column :label="t('规则管理')">
        <template #default="{ row }">
          <el-button link type="primary" @click="selected = row.id">{{ t('查看规则和用量') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-pagination v-if="total > pageSize" v-model:current-page="page" :page-size="pageSize"
      :total="total" layout="prev, pager, next" @current-change="load" />
    <p v-if="error" role="alert">{{ error }}</p>
    <NumberRuleEditor v-if="selected" :key="selected + stamp" :rule-id="selected" />
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { numberOwnerApi } from '@/api/pub/seq'
import NumberRuleEditor from './NumberRuleEditor.vue'

const { t } = useI18n()
const rows = ref<{ id: string; bizKey: string }[]>([])
const keyword = ref(''), selected = ref(''), error = ref(''), stamp = ref(identityStamp())
const page = ref(1), total = ref(0), busy = ref(false), pageSize = 10
let request = 0
async function load() {
  const turn = ++request, identity = stamp.value
  busy.value = true
  try {
    const result = await numberOwnerApi.list({ page: page.value, pageSize, keyword: keyword.value })
    if (turn === request && identity === identityStamp()) {
      rows.value = result.rows
      total.value = result.total
      error.value = ''
    }
  } catch { if (turn === request) error.value = t('无法读取编号域，请重新查询。') }
  finally { if (turn === request) busy.value = false }
}
function search() { page.value = 1; void load() }
function changed() {
  if (stamp.value === identityStamp()) return
  stamp.value = identityStamp()
  selected.value = ''; rows.value = []; total.value = 0
  search()
}
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed); void load() })
onBeforeUnmount(() => { ++request; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>

<style scoped>
.rule-search { display: flex; gap: .5rem; margin-bottom: .75rem; max-width: 28rem; }
</style>
