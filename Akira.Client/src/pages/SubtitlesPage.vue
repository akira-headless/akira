<template>
    <PageHeader
        title="Subtitle requests"
        :description="session.isAdmin ? 'Everyone\'s downloads and translations, newest first.' : 'Your downloads and translations, newest first.'"
    />

    <ErrorNotice class="mb-4" :message="action.error.value ?? requests.error.value" />

    <p v-if="requests.data.value?.length === 0" class="text-sm text-ink-400">No subtitles were asked for yet.</p>
    <div v-else class="panel overflow-hidden">
        <table class="table">
            <thead>
                <tr>
                    <th>Asked</th>
                    <th>Language</th>
                    <th>How</th>
                    <th>Status</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="request in requests.data.value ?? []" :key="request.id">
                    <td :title="when(request.createdAt)">{{ ago(request.createdAt) }}</td>
                    <td class="font-mono text-xs">{{ request.language }}</td>
                    <td>{{ words(request.kind) }} <span class="text-ink-400">via {{ request.provider }}</span></td>
                    <td>
                        <div class="flex items-center gap-3">
                            <span :class="tone[request.status]">{{ words(request.status) }}</span>
                            <div v-if="request.status === 'running'" class="h-1.5 w-24 overflow-hidden rounded bg-ink-700">
                                <div class="h-full bg-accent-500" :style="{ width: `${request.progress}%` }"></div>
                            </div>
                        </div>
                        <p v-if="request.error" class="mt-1 text-xs text-red-300">{{ request.error }}</p>
                    </td>
                    <td class="text-right">
                        <button
                            v-if="request.status === 'queued' || request.status === 'running'"
                            class="button-quiet"
                            :disabled="action.busy.value"
                            @click="cancel(request.id)"
                        >
                            Cancel
                        </button>
                    </td>
                </tr>
            </tbody>
        </table>
    </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount } from 'vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { ago, when, words } from '@/services/format'
import { subtitleService } from '@/services/services'
import { missed, useEventStore } from '@/stores/events'
import { useSessionStore } from '@/stores/session'
import type { SubtitleRequestStatus } from '@/types/api'

const session = useSessionStore()
const requests = useLoad(subtitleService.list)
const action = useAction()

const tone: Record<SubtitleRequestStatus, string> = {
    queued: 'text-ink-400',
    running: 'text-accent-400',
    completed: 'text-emerald-300',
    failed: 'text-red-300',
    cancelled: 'text-ink-400'
}

async function cancel(id: string) {
    await action.run(() => subtitleService.cancel(id))
    await requests.reload()
}

const unsubscribe = useEventStore().subscribe((event) => {
    if (event.type === 'subtitle.request.updated' || missed(event)) {
        void requests.reload()
    }
})
onBeforeUnmount(unsubscribe)
</script>
