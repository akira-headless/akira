<template>
    <div class="grid min-h-full place-items-center px-4">
        <div class="panel w-full max-w-md p-8">
            <img src="/akira-mark.svg" alt="" class="mb-5 h-10 w-auto" />
            <h1 class="text-xl font-semibold text-white">Sign in to Akira</h1>

            <div class="mt-6 flex gap-1 rounded-md bg-ink-950 p-1 text-sm">
                <button
                    v-for="option in modes"
                    :key="option.value"
                    class="flex-1 rounded px-3 py-1.5"
                    :class="mode === option.value ? 'bg-ink-700 text-white' : 'text-ink-400 hover:text-ink-200'"
                    @click="choose(option.value)"
                >
                    {{ option.label }}
                </button>
            </div>

            <div v-if="mode === 'pair'" class="mt-6">
                <p class="text-sm text-ink-400">
                    Approve this code from a device that is already signed in, under Devices, or ask an admin to.
                </p>
                <div v-if="pairing" class="mt-6 text-center">
                    <p class="font-mono text-4xl tracking-widest text-white">{{ pairing.code }}</p>
                    <p class="mt-3 text-xs text-ink-400">Waiting for approval, the code works until {{ until }}.</p>
                </div>
                <button v-else-if="!action.busy.value" class="button mt-6 w-full" @click="startPairing">Show a code</button>
            </div>

            <form v-else class="mt-6 space-y-4" @submit.prevent="signInWithKey">
                <div>
                    <label class="label" for="key">API key</label>
                    <input id="key" v-model="key" class="input font-mono" placeholder="pak_..." autocomplete="off" required />
                </div>
                <p class="text-xs text-ink-400">The key is not stored in the browser; it becomes a device you can revoke.</p>
                <button class="button w-full" :disabled="action.busy.value">Sign in</button>
            </form>

            <ErrorNotice class="mt-4" :message="action.error.value" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ErrorNotice from '@/components/ErrorNotice.vue'
import { useAction } from '@/composables/useLoad'
import { ApiError, isTransient } from '@/services/api'
import { sessionService } from '@/services/services'
import { useSessionStore } from '@/stores/session'
import type { CurrentUser, PairingStarted } from '@/types/api'

type Mode = 'pair' | 'key'
const modes: { value: Mode; label: string }[] = [
    { value: 'pair', label: 'Pair this browser' },
    { value: 'key', label: 'API key' }
]

const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const action = useAction()

const mode = ref<Mode>('pair')
const key = ref('')
const pairing = ref<PairingStarted | null>(null)
let poller: ReturnType<typeof setTimeout> | null = null
let token: string | null = null
let left = false

const until = computed(() => (pairing.value ? new Date(pairing.value.expiresAt).toLocaleTimeString() : ''))

function choose(value: Mode) {
    mode.value = value
    action.error.value = null
}

async function startPairing() {
    await action.run(async () => {
        token = null
        pairing.value = await sessionService.startPairing()
        schedule()
    })
}

function schedule() {
    if (!left) {
        poller = setTimeout(poll, (pairing.value?.pollIntervalSeconds ?? 3) * 1000)
    }
}

// The server hands the token out once, so it is kept until the sign in with it went through
async function poll() {
    const current = pairing.value
    if (!current) {
        return
    }
    try {
        if (!token) {
            const answer = await sessionService.pollPairing(current)
            token = 'token' in answer ? answer.token : null
        }
        if (token) {
            await done(await sessionService.signInWithDeviceToken(token))
            return
        }
    } catch (error) {
        // A request that got no answer says nothing about the code, which stays good until it expires
        if (!isTransient(error) || (!token && Date.now() > Date.parse(current.expiresAt))) {
            pairing.value = null
            token = null
            action.error.value =
                isTransient(error) || (error instanceof ApiError && (error.status === 410 || error.status === 404))
                    ? 'The code expired or was denied. Show a new one.'
                    : error instanceof Error
                      ? error.message
                      : String(error)
            return
        }
    }
    schedule()
}

async function signInWithKey() {
    await action.run(async () => done(await sessionService.signInWithApiKey(key.value.trim())))
}

async function done(user: CurrentUser) {
    session.signedInAs(user)
    const next = typeof route.query.next === 'string' && route.query.next.startsWith('/') && !route.query.next.startsWith('//') ? route.query.next : '/'
    await router.push(next)
}

onBeforeUnmount(() => {
    left = true
    if (poller) {
        clearTimeout(poller)
    }
})
</script>
