import { defineStore } from 'pinia'
import type { EventMessage } from '@/types/api'

type Status = 'offline' | 'connecting' | 'live'
type Handler = (event: EventMessage) => void

const KEEP = 50
const MAX_BACKOFF = 30_000

let socket: WebSocket | null = null
let retry: ReturnType<typeof setTimeout> | null = null
let backoff = 1000
const handlers = new Set<Handler>()

export const useEventStore = defineStore('events', {
    state: () => ({
        status: 'offline' as Status,
        since: null as number | null,
        recent: [] as EventMessage[]
    }),
    getters: {
        live: (state) => state.status === 'live'
    },
    actions: {
        connect() {
            if (socket) {
                return
            }
            if (retry) {
                clearTimeout(retry)
                retry = null
            }

            this.status = 'connecting'
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
            const query = this.since === null ? '' : `?since=${this.since}`
            // The session cookie goes along with the upgrade, the server checks the Origin
            const ws = new WebSocket(`${protocol}//${window.location.host}/api/v1/events/ws${query}`)
            socket = ws

            ws.onmessage = (message) => {
                const event = JSON.parse(message.data) as EventMessage
                // A message for this connection only has no place in the sequence
                if (event.sequence > 0) {
                    this.since = event.sequence
                }
                if (event.type === 'stream.ready') {
                    this.status = 'live'
                    backoff = 1000
                    return
                }
                if (event.type !== 'stream.resync') {
                    this.recent = [event, ...this.recent].slice(0, KEEP)
                }
                handlers.forEach((handler) => handler(event))
            }

            ws.onclose = (close) => {
                // A socket that was closed on purpose, or replaced since, has nothing to say about the current one
                if (socket !== ws) {
                    return
                }
                socket = null
                this.status = 'offline'
                // 1008 is a revoked or expired credential, reconnecting would only fail again
                if (close.code === 1008) {
                    return
                }
                retry = setTimeout(() => this.connect(), backoff)
                backoff = Math.min(backoff * 2, MAX_BACKOFF)
            }
        },
        disconnect() {
            if (retry) {
                clearTimeout(retry)
                retry = null
            }
            // Without its handlers the closed socket neither reconnects nor touches a newer one
            const old = socket
            socket = null
            if (old) {
                old.onclose = null
                old.onmessage = null
                old.close(1000)
            }
            this.status = 'offline'
        },
        // What the feed told belongs to the account that is signing out, the next one starts without it
        forget() {
            this.disconnect()
            this.$reset()
        },
        subscribe(handler: Handler) {
            handlers.add(handler)
            return () => handlers.delete(handler)
        }
    }
})

// stream.resync says this browser was away too long to be told what changed, so anything on screen may be old
export function missed(event: EventMessage) {
    return event.type === 'stream.resync'
}
