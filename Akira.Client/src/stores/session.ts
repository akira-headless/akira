import { defineStore } from 'pinia'
import { ApiError } from '@/services/api'
import { sessionService, setupService } from '@/services/services'
import type { CurrentUser } from '@/types/api'

export const useSessionStore = defineStore('session', {
    state: () => ({
        current: null as CurrentUser | null,
        setupRequired: false,
        checked: false,
        unreachable: null as string | null
    }),
    getters: {
        signedIn: (state) => state.current !== null,
        isAdmin: (state) => state.current?.user.role === 'admin'
    },
    actions: {
        // The cookie is HttpOnly, so only the server can say whether this browser is signed in; no answer leaves that open
        async check() {
            if (this.checked) {
                return
            }
            try {
                this.setupRequired = (await setupService.status()).setupRequired
                this.current = this.setupRequired ? null : await sessionService.me()
            } catch (error) {
                if (!(error instanceof ApiError) || error.status !== 401) {
                    this.unreachable = error instanceof Error ? error.message : String(error)
                    return
                }
                this.current = null
            }
            this.unreachable = null
            this.checked = true
        },
        signedInAs(user: CurrentUser) {
            this.current = user
            this.setupRequired = false
            this.unreachable = null
            this.checked = true
        },
        // Signed out is what the server says: while its answer is missing the cookie still works
        async signOut() {
            try {
                await sessionService.signOut()
            } catch (error) {
                if (!(error instanceof ApiError) || error.status !== 401) {
                    throw error
                }
            }
            this.current = null
        },
        lost() {
            this.current = null
        }
    }
})
