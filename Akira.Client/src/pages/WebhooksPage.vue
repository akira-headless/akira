<template>
    <PageHeader title="Webhooks" description="Urls of other tools, such as Home Assistant, that get events as they happen." />

    <form class="panel mb-6 space-y-4 p-4" @submit.prevent="create">
        <div class="flex flex-wrap items-end gap-3">
            <div class="w-48">
                <label class="label" for="name">Name</label>
                <input id="name" v-model="name" class="input" maxlength="100" required />
            </div>
            <div class="min-w-72 flex-1">
                <label class="label" for="url">Url</label>
                <input id="url" v-model="url" class="input" type="url" placeholder="https://..." required />
            </div>
            <button class="button" :disabled="action.busy.value">Add webhook</button>
        </div>
        <fieldset>
            <legend class="label">Events (none ticked is every event)</legend>
            <div class="flex flex-wrap gap-x-5 gap-y-1.5">
                <label v-for="type in eventTypes" :key="type" class="flex items-center gap-2 text-sm">
                    <input v-model="types" type="checkbox" :value="type" class="size-4 accent-accent-500" />
                    <span class="font-mono text-xs">{{ type }}</span>
                </label>
            </div>
        </fieldset>
    </form>

    <SecretOnce
        v-if="secret"
        class="mb-6"
        :secret="secret"
        message="The signing secret, shown only now. The receiver checks X-Akira-Signature: HMAC-SHA256 of 'timestamp.body' with it."
    />
    <ErrorNotice class="mb-4" :message="action.error.value ?? webhooks.error.value" />

    <p v-if="webhooks.data.value?.length === 0" class="text-sm text-ink-400">No webhooks yet.</p>
    <div class="space-y-3">
        <div v-for="webhook in webhooks.data.value ?? []" :key="webhook.id" class="panel p-4">
            <div class="flex flex-wrap items-center gap-3">
                <span class="size-2 rounded-full" :class="tone(webhook)"></span>
                <span class="font-medium text-white">{{ webhook.name }}</span>
                <span class="truncate font-mono text-xs text-ink-400">{{ webhook.url }}</span>
                <span class="flex-1"></span>
                <button class="button-quiet" :disabled="action.busy.value" @click="test(webhook)">Send a test</button>
                <button class="button-quiet" :disabled="action.busy.value" @click="toggle(webhook)">{{ webhook.enabled ? 'Pause' : 'Resume' }}</button>
                <button class="button-quiet" :disabled="action.busy.value" @click="rotate(webhook)">New secret</button>
                <ConfirmButton label="Delete" @confirm="remove(webhook)" />
            </div>
            <p class="mt-2 text-xs text-ink-400">
                {{ webhook.eventTypes.length ? webhook.eventTypes.join(', ') : 'Every event' }} ·
                last delivered {{ ago(webhook.lastDeliveredAt) }}<span v-if="webhook.lastStatusCode"> ({{ webhook.lastStatusCode }})</span>
            </p>
            <p v-if="webhook.lastError" class="mt-1 text-xs text-red-300">
                {{ webhook.lastError }}
                <span v-if="webhook.nextAttemptAt"> Next try {{ ago(webhook.nextAttemptAt) }}.</span>
            </p>
            <p v-if="results[webhook.id]" class="mt-1 text-xs" :class="results[webhook.id]!.succeeded ? 'text-emerald-300' : 'text-red-300'">
                Test: {{ results[webhook.id]!.succeeded ? `answered ${results[webhook.id]!.statusCode}` : results[webhook.id]!.error }}
            </p>
        </div>
    </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import ConfirmButton from '@/components/ConfirmButton.vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import SecretOnce from '@/components/SecretOnce.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { ago } from '@/services/format'
import { webhookService } from '@/services/services'
import type { OutgoingWebhook, OutgoingWebhookTestResult } from '@/types/api'

const eventTypes = [
    'library.item.added',
    'library.item.updated',
    'library.item.removed',
    'library.file.replaced',
    'playback.started',
    'playback.stopped',
    'watchstate.changed',
    'trackpreference.changed',
    'action.completed',
    'scan.progress',
    'subtitle.request.updated'
]

const webhooks = useLoad(webhookService.list)
const action = useAction()
const name = ref('')
const url = ref('')
const types = ref<string[]>([])
const secret = ref<string | null>(null)
const results = reactive<Record<string, OutgoingWebhookTestResult>>({})

function tone(webhook: OutgoingWebhook) {
    if (!webhook.enabled) return 'bg-ink-600'
    return webhook.consecutiveFailures > 0 ? 'bg-red-400' : 'bg-emerald-400'
}

async function create() {
    secret.value = null
    if (await action.run(async () => (secret.value = (await webhookService.create(name.value.trim(), url.value.trim(), types.value)).secret))) {
        name.value = ''
        url.value = ''
        types.value = []
        await webhooks.reload()
    }
}

async function test(webhook: OutgoingWebhook) {
    await action.run(async () => (results[webhook.id] = await webhookService.test(webhook.id)))
}

async function toggle(webhook: OutgoingWebhook) {
    await action.run(() => webhookService.update(webhook.id, { enabled: !webhook.enabled }))
    await webhooks.reload()
}

async function rotate(webhook: OutgoingWebhook) {
    await action.run(async () => (secret.value = (await webhookService.rotate(webhook.id)).secret))
}

async function remove(webhook: OutgoingWebhook) {
    await action.run(() => webhookService.remove(webhook.id))
    await webhooks.reload()
}
</script>
