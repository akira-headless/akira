<template>
    <PageHeader title="API keys" description="For scripts and tools. A key acts as the user who made it, and is shown only once." />

    <form class="panel mb-6 flex items-end gap-3 p-4" @submit.prevent="create">
        <div class="w-72">
            <label class="label" for="name">Name</label>
            <input id="name" v-model="name" class="input" maxlength="100" placeholder="What uses it" required />
        </div>
        <button class="button" :disabled="action.busy.value">Create key</button>
    </form>

    <SecretOnce v-if="created" class="mb-6" :secret="created" message="Shown only now. Copy it into the tool that needs it." />
    <ErrorNotice class="mb-4" :message="action.error.value ?? keys.error.value" />

    <div class="panel overflow-hidden">
        <table class="table">
            <thead>
                <tr>
                    <th>Name</th>
                    <th>Starts with</th>
                    <th v-if="session.isAdmin">User</th>
                    <th>Created</th>
                    <th>Last used</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="key in keys.data.value ?? []" :key="key.id">
                    <td class="text-white">{{ key.name }}</td>
                    <td class="font-mono text-xs">{{ key.prefix }}…</td>
                    <td v-if="session.isAdmin">{{ key.userName }}</td>
                    <td :title="when(key.createdAt)">{{ ago(key.createdAt) }}</td>
                    <td :title="when(key.lastUsedAt)">{{ ago(key.lastUsedAt) }}</td>
                    <td class="text-right"><ConfirmButton label="Revoke" @confirm="revoke(key.id)" /></td>
                </tr>
            </tbody>
        </table>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import ConfirmButton from '@/components/ConfirmButton.vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import SecretOnce from '@/components/SecretOnce.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { ago, when } from '@/services/format'
import { apiKeyService } from '@/services/services'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const keys = useLoad(apiKeyService.list)
const action = useAction()
const name = ref('')
const created = ref<string | null>(null)

async function create() {
    created.value = null
    if (await action.run(async () => (created.value = (await apiKeyService.create(name.value.trim())).key))) {
        name.value = ''
        await keys.reload()
    }
}

async function revoke(id: string) {
    await action.run(() => apiKeyService.revoke(id))
    await keys.reload()
}
</script>
