<template>
    <PageHeader title="Overview" :description="health.data.value ? `Server ${health.data.value.version}` : undefined" />

    <div class="grid gap-4 md:grid-cols-3">
        <div v-for="check in checks" :key="check.label" class="panel p-4">
            <p class="label">{{ check.label }}</p>
            <p class="mt-1 flex items-center gap-2 text-sm">
                <span class="size-2 rounded-full" :class="tone(check.state)"></span>
                <span class="text-white">{{ check.state ? words(check.state) : 'Checking' }}</span>
            </p>
            <p class="mt-2 text-xs text-ink-400">{{ check.note }}</p>
        </div>
    </div>

    <RouterLink v-if="pending.length" :to="{ name: 'devices' }" class="panel mt-4 block border-amber-700/60 p-4 hover:bg-ink-800">
        <p class="text-sm text-amber-200">
            {{ pending.length === 1 ? 'A device is' : `${pending.length} devices are` }} waiting to be approved:
            {{ pending.map((p) => `${p.deviceName} (${p.code})`).join(', ') }}
        </p>
    </RouterLink>

    <NowPlayingPanel v-if="session.isAdmin" class="mt-6" />

    <div class="mt-6">
        <PluginsPanel v-if="session.isAdmin" />
        <section v-else class="panel p-4">
            <h2 class="text-sm font-medium text-white">Subtitle providers</h2>
            <ul class="mt-3 space-y-2 text-sm">
                <li v-for="provider in providers.data.value ?? []" :key="provider.key" class="flex items-center justify-between">
                    <span>{{ provider.name }} <span class="text-xs text-ink-400">{{ provider.kind }}</span></span>
                    <span :class="provider.isConfigured ? 'text-emerald-300' : 'text-ink-400'">
                        {{ provider.isConfigured ? 'Configured' : 'Not configured' }}
                    </span>
                </li>
            </ul>
        </section>
    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import NowPlayingPanel from '@/components/NowPlayingPanel.vue'
import PageHeader from '@/components/PageHeader.vue'
import PluginsPanel from '@/components/PluginsPanel.vue'
import { useLoad } from '@/composables/useLoad'
import { usePoll } from '@/composables/usePoll'
import { words } from '@/services/format'
import { deviceService, healthService } from '@/services/services'
import { useSessionStore } from '@/stores/session'
import type { HealthState, PendingPairing } from '@/types/api'

const session = useSessionStore()
const health = useLoad(healthService.get)
const providers = useLoad(healthService.subtitleProviders)
const pending = ref<PendingPairing[]>([])

const checks = computed(() => [
    { label: 'Server', state: health.data.value?.status, note: 'Answers and can reach its database.' },
    { label: 'Database', state: health.data.value?.database, note: 'SQLite or MariaDB, as configured.' },
    { label: 'ffprobe', state: health.data.value?.ffprobe, note: 'Reads the tracks of new files. Without it they play, but unprobed.' }
])

function tone(state: HealthState | undefined) {
    return state === 'healthy' ? 'bg-emerald-400' : state === 'unhealthy' ? 'bg-red-400' : 'bg-ink-600'
}

// Pairings have no event of their own, so an admin's overview asks every few seconds; a failed answer keeps the list it had
async function loadPending() {
    if (session.isAdmin) {
        pending.value = await deviceService.pending().catch(() => pending.value)
    }
}

usePoll(loadPending, 5000)
</script>
