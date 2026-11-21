<template>
    <header class="mb-6 flex items-end justify-between gap-4">
        <div>
            <h1 class="text-xl font-semibold text-white">{{ title }}</h1>
            <p v-if="description" class="mt-1 text-sm text-ink-400">{{ description }}</p>
        </div>
        <div class="flex items-center gap-2"><slot /></div>
    </header>
</template>

<script setup lang="ts">
defineProps<{ title: string; description?: string }>()
</script>
