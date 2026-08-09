<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'

const handles = [
  { id: 'top', position: Position.Top },
  { id: 'right', position: Position.Right },
  { id: 'bottom', position: Position.Bottom },
  { id: 'left', position: Position.Left },
] as const
</script>

<template>
  <Handle
    v-for="handle in handles"
    :id="handle.id"
    :key="handle.id"
    type="source"
    :position="handle.position"
  />
</template>
