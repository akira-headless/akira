<template>
    <PageHeader title="Live events" description="What happens on the server, as it happens. The list starts when this browser connected." />

    <section class="panel p-4">
        <div class="flex items-center justify-between">
            <h2 class="text-sm font-medium text-white">Recent</h2>
            <span class="text-xs text-ink-400">{{ events.status === 'live' ? 'Connected' : words(events.status) }}</span>
        </div>
        <p v-if="!events.recent.length" class="mt-3 text-sm text-ink-400">Nothing happened since this browser connected.</p>
        <ul class="mt-3 divide-y divide-ink-800 text-sm">
            <li v-for="event in events.recent" :key="event.sequence" class="grid min-w-0 gap-x-3 gap-y-0.5 py-2 sm:grid-cols-[6rem_14rem_minmax(0,1fr)]">
                <span class="text-xs text-ink-400">{{ new Date(event.createdAt).toLocaleTimeString() }}</span>
                <span class="font-mono text-xs break-all text-accent-400">{{ event.type }}</span>
                <span class="min-w-0 text-xs break-words text-ink-400">{{ summary(event.payload) }}</span>
            </li>
        </ul>
    </section>
</template>

<script setup lang="ts">
import PageHeader from '@/components/PageHeader.vue'
import { words } from '@/services/format'
import { useEventStore } from '@/stores/events'

const events = useEventStore()

function summary(payload: unknown) {
    if (!payload || typeof payload !== 'object') {
        return ''
    }
    return Object.entries(payload as Record<string, unknown>)
        .filter(([, value]) => value !== null && typeof value !== 'object')
        .map(([key, value]) => `${key} ${value}`)
        .join(' · ')
}
</script>
