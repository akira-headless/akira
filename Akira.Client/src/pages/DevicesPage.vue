<template>
    <PageHeader title="Devices" :description="session.isAdmin ? 'Every paired device, browser and shared screen.' : 'Your paired devices and browsers.'" />

    <section class="panel mb-6 p-4">
        <h2 class="text-sm font-medium text-white">Approve a code</h2>
        <p class="mt-1 text-sm text-ink-400">The code a new device shows, like K7Q-4MX.</p>

        <div v-if="session.isAdmin && pending.length" class="mt-4 space-y-2">
            <div v-for="waiting in pending" :key="waiting.code" class="flex flex-wrap items-center gap-3 rounded-md bg-ink-950 px-3 py-2 text-sm">
                <span class="font-mono text-white">{{ waiting.code }}</span>
                <span>{{ waiting.deviceName }}</span>
                <span class="text-xs text-ink-400">{{ waiting.client }}{{ waiting.platform ? `, ${waiting.platform}` : '' }} · {{ ago(waiting.createdAt) }}</span>
                <span class="flex-1"></span>
                <button class="button-quiet" @click="code = waiting.code">Use this code</button>
                <button class="button-danger" @click="deny(waiting.code)">Deny</button>
            </div>
        </div>

        <form class="mt-4 flex flex-wrap items-end gap-3" @submit.prevent="approve">
            <div class="w-36">
                <label class="label" for="code">Code</label>
                <input id="code" v-model="code" class="input font-mono uppercase" placeholder="K7Q-4MX" required />
            </div>
            <template v-if="session.isAdmin">
                <div class="w-40">
                    <label class="label" for="kind">Kind</label>
                    <select id="kind" v-model="kind" class="input">
                        <option value="personal">Personal</option>
                        <option value="shared">Shared, with profiles</option>
                    </select>
                </div>
                <div v-if="kind === 'personal'" class="w-48">
                    <label class="label" for="owner">Owner</label>
                    <select id="owner" v-model="owner" class="input">
                        <option v-for="user in users" :key="user.id" :value="user.id">{{ user.name }}</option>
                    </select>
                </div>
            </template>
            <button class="button" :disabled="action.busy.value">Approve</button>
        </form>
        <ErrorNotice class="mt-3" :message="action.error.value" />
        <p v-if="approved" class="mt-3 text-sm text-emerald-300">Approved {{ approved }}. It signs in within a few seconds.</p>
    </section>

    <ConnectAppPanel class="mb-6" @created="devices.reload()" />

    <ErrorNotice :message="devices.error.value" />
    <div class="panel overflow-hidden">
        <table class="table">
            <thead>
                <tr>
                    <th>Device</th>
                    <th>Client</th>
                    <th>Kind</th>
                    <th v-if="session.isAdmin">Owner</th>
                    <th>Last seen</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="device in devices.data.value ?? []" :key="device.id">
                    <td class="text-white">
                        {{ device.name }}
                        <span v-if="device.id === session.current?.device?.id" class="ml-1 text-xs text-accent-400">this browser</span>
                    </td>
                    <td>{{ device.client }}{{ device.clientVersion ? ` ${device.clientVersion}` : '' }}<span v-if="device.platform" class="text-ink-400">, {{ device.platform }}</span></td>
                    <td>{{ words(device.kind) }}</td>
                    <td v-if="session.isAdmin">{{ device.userName ?? '' }}</td>
                    <td :title="when(device.lastSeenAt)">{{ ago(device.lastSeenAt) }}</td>
                    <td class="text-right">
                        <ConfirmButton label="Revoke" :disabled="device.id === session.current?.device?.id" @confirm="revoke(device.id)" />
                    </td>
                </tr>
            </tbody>
        </table>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import ConfirmButton from '@/components/ConfirmButton.vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import ConnectAppPanel from '@/components/ConnectAppPanel.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { usePoll } from '@/composables/usePoll'
import { ago, when, words } from '@/services/format'
import { deviceService, userService } from '@/services/services'
import { useSessionStore } from '@/stores/session'
import type { DeviceKind, PendingPairing, UserSummary } from '@/types/api'

const session = useSessionStore()
const devices = useLoad(deviceService.list)
const action = useAction()

const pending = ref<PendingPairing[]>([])
const users = ref<UserSummary[]>([])
const code = ref('')
const kind = ref<DeviceKind>('personal')
const owner = ref<string | null>(session.current?.user.id ?? null)
const approved = ref<string | null>(null)

// A failed answer keeps the list it had, and the next one asks again
async function loadPending() {
    if (session.isAdmin) {
        pending.value = await deviceService.pending().catch(() => pending.value)
    }
}

async function approve() {
    approved.value = null
    const userId = session.isAdmin && kind.value === 'personal' ? owner.value : null
    const done = await action.run(async () => {
        const device = await deviceService.approve(code.value.trim().toUpperCase(), session.isAdmin ? kind.value : 'personal', userId)
        approved.value = device.name
    })
    if (done) {
        code.value = ''
        await Promise.all([loadPending(), devices.reload()])
    }
}

async function deny(pendingCode: string) {
    await action.run(() => deviceService.deny(pendingCode))
    await loadPending()
}

async function revoke(id: string) {
    await action.run(() => deviceService.revoke(id))
    await devices.reload()
}

usePoll(loadPending, 3000)
onMounted(async () => {
    if (session.isAdmin) {
        users.value = await userService.list().catch((caught: unknown) => {
            action.error.value = `The owners could not be listed. ${caught instanceof Error ? caught.message : String(caught)}`
            return []
        })
    }
})
</script>
