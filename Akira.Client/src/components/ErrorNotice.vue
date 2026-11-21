<template>
    <p v-if="message" class="rounded-md border border-red-900 bg-red-950/50 px-3 py-2 text-sm text-red-200">{{ message }}</p>
</template>

<script setup lang="ts">
defineProps<{ message: string | null }>()
</script>
