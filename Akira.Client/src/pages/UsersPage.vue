<template>
    <PageHeader title="Users" description="Everyone in the household. Each has their own watch state, languages and devices." />

    <form class="panel mb-6 flex flex-wrap items-end gap-3 p-4" @submit.prevent="create">
        <div class="w-56">
            <label class="label" for="name">Name</label>
            <input id="name" v-model="name" class="input" maxlength="100" required />
        </div>
        <div class="w-32">
            <label class="label" for="role">Role</label>
            <select id="role" v-model="role" class="input">
                <option value="user">User</option>
                <option value="admin">Admin</option>
            </select>
        </div>
        <div class="w-40">
            <label class="label" for="pin">PIN (optional)</label>
            <input id="pin" v-model="pin" class="input" inputmode="numeric" />
        </div>
        <button class="button" :disabled="action.busy.value">Add user</button>
    </form>

    <ErrorNotice class="mb-4" :message="action.error.value ?? users.error.value" />

    <div class="panel overflow-hidden">
        <table class="table">
            <thead>
                <tr>
                    <th>Name</th>
                    <th>Role</th>
                    <th>PIN</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="user in users.data.value ?? []" :key="user.id">
                    <td>
                        <input
                            v-if="editing === user.id"
                            v-model="editName"
                            class="input max-w-xs"
                            maxlength="100"
                            @keydown.enter="rename(user)"
                            @keydown.esc="editing = null"
                        />
                        <button v-else class="text-white hover:underline" title="Rename" @click="startRename(user)">{{ user.name }}</button>
                        <span v-if="user.id === session.current?.user.id" class="ml-2 text-xs text-accent-400">you</span>
                    </td>
                    <td>
                        <select class="input w-28" :value="user.role" @change="setRole(user, ($event.target as HTMLSelectElement).value as UserRole)">
                            <option value="user">User</option>
                            <option value="admin">Admin</option>
                        </select>
                    </td>
                    <td>
                        <div class="flex items-center gap-2">
                            <span class="w-10 text-ink-400">{{ user.hasPin ? 'Set' : 'None' }}</span>
                            <input v-model="pins[user.id]" class="input w-24" inputmode="numeric" placeholder="New PIN" />
                            <button class="button-quiet" :disabled="!pins[user.id]" @click="setPin(user, pins[user.id] ?? null)">Set</button>
                            <button v-if="user.hasPin" class="button-quiet" @click="setPin(user, null)">Remove</button>
                        </div>
                    </td>
                    <td class="text-right">
                        <ConfirmButton
                            label="Delete"
                            confirm-label="Delete with devices and keys?"
                            :disabled="user.id === session.current?.user.id"
                            @confirm="remove(user)"
                        />
                    </td>
                </tr>
            </tbody>
        </table>
    </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import ConfirmButton from '@/components/ConfirmButton.vue'
import ErrorNotice from '@/components/ErrorNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useAction, useLoad } from '@/composables/useLoad'
import { userService } from '@/services/services'
import { useSessionStore } from '@/stores/session'
import type { UserRole, UserSummary } from '@/types/api'

const session = useSessionStore()
const users = useLoad(userService.list)
const action = useAction()

const name = ref('')
const role = ref<UserRole>('user')
const pin = ref('')
const editing = ref<string | null>(null)
const editName = ref('')
const pins = reactive<Record<string, string>>({})

async function create() {
    if (await action.run(() => userService.create(name.value.trim(), role.value, pin.value.trim() || null))) {
        name.value = ''
        pin.value = ''
        await users.reload()
    }
}

function startRename(user: UserSummary) {
    editing.value = user.id
    editName.value = user.name
}

async function rename(user: UserSummary) {
    if (await action.run(() => userService.update(user.id, { name: editName.value.trim() }))) {
        editing.value = null
        await users.reload()
    }
}

async function setRole(user: UserSummary, newRole: UserRole) {
    await action.run(() => userService.update(user.id, { role: newRole }))
    await users.reload()
}

async function setPin(user: UserSummary, newPin: string | null) {
    if (await action.run(() => userService.setPin(user.id, newPin?.trim() || null))) {
        delete pins[user.id]
        await users.reload()
    }
}

async function remove(user: UserSummary) {
    await action.run(() => userService.remove(user.id))
    await users.reload()
}
</script>
