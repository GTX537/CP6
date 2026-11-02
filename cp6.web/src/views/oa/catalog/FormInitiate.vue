<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import PersonalFormEditor from './PersonalFormEditor.vue'
import LegacyFormInitiate from './LegacyFormInitiate.vue'
const route = useRoute()
const formKey = computed(() => typeof route.query.formKey === 'string' ? route.query.formKey : '')
</script>
<template>
  <PersonalFormEditor v-if="route.query.definition === 'published'" :form-key="formKey" />
  <LegacyFormInitiate v-else />
</template>
