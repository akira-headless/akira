<template>
    <PageHeader title="Trakt" description="Scrobbles what you play and keeps your watched history in step with your Trakt account." />

    <ErrorNotice class="mb-4" :message="action.error.value ?? status.error.value" />

    <div v-if="status.data.value" class="panel max-w-2xl p-5">
        <p v-if="!status.data.value.configured" class="text-sm text-ink-400">
            Trakt is not set up on this server. An admin registers an app on trakt.tv (Settings, Your API Apps) and sets
            <code class="text-ink-200">Trakt:ClientId</code> and <code class="text-ink-200">Trakt:ClientSecret</code>.
        </p>

        <template v-else-if="status.data.value.pending">
            <p class="text-sm">Go to <a :href="status.data.value.pending.verificationUrl" target="_blank" rel="noopener" class="text-accent-400 hover:underline">{{ status.data.value.pending.verificationUrl }}</a> and enter:</p>
            <p class="mt-4 font-mono text-4xl tracking-widest text-white">{{ status.data.value.pending.userCode }}</p>
            <p class="mt-3 text-xs text-ink-400">Waiting for Trakt. The code works until {{ new Date(status.data.value.pending.expiresAt).toLocaleTimeString() }}.</p>
        </template>

        <template v-else-if="status.data.value.linked">
            <p class="text-sm">Linked to <span class="font-medium text-white">{{ status.data.value.username }}</span> {{ ago(status.data.value.linkedAt) }}.</p>
            <p class="mt-1 text-xs text-ink-400">
                History imported {{ ago(status.data.value.lastImportAt) }} · last sent to Trakt {{ ago(status.data.value.lastSyncedAt) }}
            </p>
            <p v-if="status.data.value.lastError" class="mt-2 text-xs text-red-300">{{ status.data.value.lastError }}</p>
            <p v-if="imported" class="mt-2 text-xs text-emerald-300">{{ imported }}</p>
            <div class="mt-4 flex gap-2">
                <button class="button-quiet" :disabled="action.busy.value" @click="importNow">Import watched history</button>
                <ConfirmButton label="Unlink" @confirm="unlink" />
            </div>
        </template>

        <template v-else>
            <p class="text-sm text-ink-400">
                Linking imports what you watched on Trakt. From then on what you play here is scrobbled, and marking something played or unplayed by
                hand changes your Trakt history too.
            </p>
            <button class="button mt-4" :disabled="action.busy.value" @click="link">Link Trakt</button>
        </template>
    </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import ConfirmButton from '@/components/ConfirmButton.vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { ago } from '@/services/format'
import { traktService } from '@/services/services'

const status = useLoad(traktService.status)
const action = useAction()
const imported = ref<string | null>(null)
let poller: ReturnType<typeof setTimeout> | null = null

// While a code waits, Trakt is asked again every interval it gave
watch(
    () => status.data.value?.pending,
    (pending) => {
        if (poller) {
            clearTimeout(poller)
            poller = null
        }
        if (pending) {
            poller = setTimeout(poll, pending.intervalSeconds * 1000)
        }
    }
)

async function link() {
    await action.run(async () => (status.data.value = await traktService.link()))
}

async function poll() {
    if (!(await action.run(async () => (status.data.value = await traktService.poll())))) {
        await status.reload()
    }
}

async function importNow() {
    imported.value = null
    await action.run(async () => {
        const result = await traktService.importHistory()
        imported.value = `Marked ${result.movies} movies and ${result.episodes} episodes played.`
    })
    await status.reload()
}

async function unlink() {
    await action.run(() => traktService.unlink())
    await status.reload()
}

onBeforeUnmount(() => {
    if (poller) {
        clearTimeout(poller)
    }
})
</script>
