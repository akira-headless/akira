<template>
    <PageHeader title="Audit log" description="Every delete, unmonitor and mark played, with who did it and what Radarr and Sonarr answered." />

    <ErrorNotice class="mb-4" :message="page.error.value" />

    <p v-if="page.data.value?.total === 0" class="text-sm text-ink-400">Nothing was removed or marked yet.</p>
    <div v-else class="panel overflow-hidden">
        <table class="table">
            <thead>
                <tr>
                    <th>When</th>
                    <th>Who</th>
                    <th>What</th>
                    <th>Item</th>
                    <th>Outcome</th>
                </tr>
            </thead>
            <tbody>
                <template v-for="entry in page.data.value?.items ?? []" :key="entry.id">
                    <tr class="cursor-pointer hover:bg-ink-800" @click="open = open === entry.id ? null : entry.id">
                        <td :title="when(entry.createdAt)">{{ ago(entry.createdAt) }}</td>
                        <td>{{ entry.userName }}</td>
                        <td>{{ words(entry.action) }}<span v-if="entry.scope" class="text-ink-400"> {{ entry.scope }}</span></td>
                        <td class="text-white">{{ entry.title }}</td>
                        <td :class="tone[entry.status]">{{ words(entry.status) }}</td>
                    </tr>
                    <tr v-if="open === entry.id">
                        <td colspan="5" class="bg-ink-950">
                            <pre class="overflow-x-auto text-xs text-ink-400">{{ JSON.stringify(entry.details, null, 2) }}</pre>
                        </td>
                    </tr>
                </template>
            </tbody>
        </table>
    </div>

    <div v-if="page.data.value && page.data.value.total > limit" class="mt-4 flex items-center gap-3 text-sm">
        <button class="button-quiet" :disabled="offset === 0" @click="move(-limit)">Newer</button>
        <span class="text-ink-400">{{ offset + 1 }} to {{ Math.min(offset + limit, page.data.value.total) }} of {{ page.data.value.total }}</span>
        <button class="button-quiet" :disabled="offset + limit >= page.data.value.total" @click="move(limit)">Older</button>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useLoad } from '@/composables/useLoad'
import { ago, when, words } from '@/services/format'
import { auditService } from '@/services/services'
import type { ActionStatus } from '@/types/api'

const limit = 50
const offset = ref(0)
const open = ref<string | null>(null)
const page = useLoad(() => auditService.list(offset.value, limit))

const tone: Record<ActionStatus, string> = {
    succeeded: 'text-emerald-300',
    partiallySucceeded: 'text-amber-300',
    failed: 'text-red-300',
    planned: 'text-ink-400',
    started: 'text-amber-300'
}

async function move(by: number) {
    offset.value = Math.max(0, offset.value + by)
    await page.reload()
}
</script>
