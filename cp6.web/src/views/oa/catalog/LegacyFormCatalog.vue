<template>
  <div class="form-catalog">
    <!-- Favorites section -->
    <el-card v-if="favorites.length" shadow="never" class="fav-card">
      <template #header>
        <span class="section-title">⭐ {{ t('oa.catalog.favorites') }}</span>
      </template>
      <div class="card-grid">
        <el-card
          v-for="card in favorites"
          :key="card.formKey"
          shadow="hover"
          class="form-card"
        >
          <div class="card-header">
            <span class="form-name">{{ card.formName }}</span>
            <el-button
              v-permission="'oa-form-catalog:favorite'"
              :icon="card.favorite ? StarFilled : Star"
              link
              :type="card.favorite ? 'warning' : 'default'"
              @click.stop="toggleFavorite(card)"
            />
          </div>
          <div class="card-actions">
            <el-button type="primary" size="small" @click="goInitiate(card.formKey)">
              {{ t('oa.catalog.fill') }}
            </el-button>
          </div>
        </el-card>
      </div>
    </el-card>

    <!-- Category tree -->
    <div v-if="loading" class="loading-placeholder" v-loading="loading" style="min-height: 120px" />

    <el-collapse v-else-if="tree.length" v-model="openCategories" class="catalog-collapse">
      <el-collapse-item
        v-for="node in tree"
        :key="node.category"
        :name="node.category"
        :title="node.category"
      >
        <div v-for="sub in node.subs" :key="sub.subCategory" class="sub-section">
          <div class="sub-label">{{ sub.subCategory }}</div>
          <div class="card-grid">
            <el-card
              v-for="card in sub.forms"
              :key="card.formKey"
              shadow="hover"
              class="form-card"
            >
              <div class="card-header">
                <span class="form-name">{{ card.formName }}</span>
                <el-button
                  v-permission="'oa-form-catalog:favorite'"
                  :icon="card.favorite ? StarFilled : Star"
                  link
                  :type="card.favorite ? 'warning' : 'default'"
                  @click.stop="toggleFavorite(card)"
                />
              </div>
              <div class="card-actions">
                <el-button type="primary" size="small" @click="goInitiate(card.formKey)">
                  {{ t('oa.catalog.fill') }}
                </el-button>
              </div>
            </el-card>
          </div>
        </div>
      </el-collapse-item>
    </el-collapse>

    <CpEmpty v-else-if="!loading" :text="t('oa.catalog.empty')" />
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Star, StarFilled } from '@element-plus/icons-vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import { catalogApi } from '@/api/oa/catalog'
import type { CatalogNode, FormCard } from '@/types/oa/advanced'

const { t } = useI18n()
const router = useRouter()

const tree = ref<CatalogNode[]>([])
const loading = ref(false)
const openCategories = ref<string[]>([])

const favorites = computed<FormCard[]>(() => {
  const result: FormCard[] = []
  for (const node of tree.value) {
    for (const sub of node.subs) {
      for (const card of sub.forms) {
        if (card.favorite) result.push(card)
      }
    }
  }
  return result
})

async function load() {
  loading.value = true
  try {
    const res = await catalogApi.tree()
    tree.value = ((res as any).data as CatalogNode[]) || []
    // expand first category by default
    const first = tree.value[0]
    if (first) {
      openCategories.value = [first.category]
    }
  } finally {
    loading.value = false
  }
}

async function toggleFavorite(card: FormCard) {
  const newVal = !card.favorite
  // optimistic update
  card.favorite = newVal
  try {
    await catalogApi.favorite(card.formKey, newVal)
  } catch {
    // revert on error
    card.favorite = !newVal
  }
}

function goInitiate(formKey: string) {
  router.push('/oa/form-initiate?formKey=' + formKey)
}

onMounted(load)
</script>

<style scoped>
.form-catalog {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.fav-card {
  margin: 0;
}

.section-title {
  font-weight: 600;
  font-size: 14px;
  color: var(--cp-ink);
}

.catalog-collapse {
  border: 1px solid var(--cp-line-soft);
  border-radius: var(--cp-r-md);
}

.sub-section {
  margin-bottom: 12px;
}

.sub-label {
  font-size: 12px;
  color: var(--cp-muted);
  font-weight: 500;
  margin-bottom: 8px;
  padding-left: 2px;
}

.card-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.form-card {
  width: 200px;
  min-width: 160px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  margin-bottom: 8px;
}

.form-name {
  font-size: 13px;
  font-weight: 500;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-actions {
  display: flex;
  justify-content: flex-end;
}

.loading-placeholder {
  width: 100%;
}
</style>
