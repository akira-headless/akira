import { api } from '@/services/api'
import type {
    ApiKeySummary,
    AuditPage,
    CreatedApiKey,
    CreatedOutgoingWebhook,
    CurrentUser,
    DeviceKind,
    DeviceSummary,
    HealthResponse,
    BridgeSummary,
    LinkCreated,
    NowPlaying,
    PluginOverview,
    OutgoingWebhook,
    OutgoingWebhookTestResult,
    PairingCompleted,
    PairingPending,
    PairingStarted,
    PendingPairing,
    ServerRemovalSettings,
    SetupResponse,
    SetupStatus,
    SubtitleProvider,
    SubtitleRequest,
    TraktImportResult,
    TraktStatus,
    UserRole,
    UserSummary
} from '@/types/api'

const browserName = () => {
    const agent = navigator.userAgent
    const browser = /Edg\//.test(agent) ? 'Edge' : /Firefox\//.test(agent) ? 'Firefox' : /Chrome\//.test(agent) ? 'Chrome' : /Safari\//.test(agent) ? 'Safari' : 'Browser'
    const platform = /Windows/.test(agent) ? 'Windows' : /Mac OS/.test(agent) ? 'macOS' : /Android/.test(agent) ? 'Android' : /Linux/.test(agent) ? 'Linux' : null
    return { name: platform ? `${browser} on ${platform}` : browser, platform }
}

export const setupService = {
    status: () => api.get<SetupStatus>('/setup'),
    complete: (setupCode: string, name: string, pin: string | null) => api.post<SetupResponse>('/setup', { setupCode, name, pin })
}

export const sessionService = {
    me: () => api.get<CurrentUser>('/users/me'),
    signInWithApiKey: (key: string) => api.post<CurrentUser>('/auth/browser-session', browserName(), { 'X-Api-Key': key }),
    signInWithDeviceToken: (token: string) => api.post<CurrentUser>('/auth/browser-session', {}, { Authorization: `Bearer ${token}` }),
    signOut: () => api.delete('/auth/browser-session'),
    startPairing: () => {
        const { name, platform } = browserName()
        return api.post<PairingStarted>('/auth/pairings', { deviceName: name, client: 'web', clientVersion: null, platform })
    },
    // 202 while waiting answers the pending state, 200 the token
    pollPairing: (pairing: PairingStarted) =>
        api.post<PairingCompleted | PairingPending>(`/auth/pairings/${pairing.pairingId}/token`, { pollSecret: pairing.pollSecret })
}

export const pluginService = {
    list: () => api.get<PluginOverview>('/plugins')
}

export const nowPlayingService = {
    list: () => api.get<NowPlaying[]>('/sessions')
}

export const healthService = {
    get: () => api.get<HealthResponse>('/health'),
    subtitleProviders: () => api.get<SubtitleProvider[]>('/subtitle-providers')
}

export const userService = {
    list: () => api.get<UserSummary[]>('/users'),
    create: (name: string, role: UserRole, pin: string | null) => api.post<UserSummary>('/users', { name, role, pin }),
    update: (id: string, changes: { name?: string; role?: UserRole }) => api.patch<UserSummary>(`/users/${id}`, changes),
    remove: (id: string) => api.delete(`/users/${id}`),
    setPin: (id: string, newPin: string | null) => api.put<UserSummary>(`/users/${id}/pin`, { currentPin: null, newPin })
}

export const deviceService = {
    list: () => api.get<DeviceSummary[]>('/devices'),
    revoke: (id: string) => api.delete(`/devices/${id}`),
    pending: () => api.get<PendingPairing[]>('/auth/pairings'),
    approve: (code: string, kind: DeviceKind, userId: string | null) => api.post<DeviceSummary>('/auth/pairings/approve', { code, kind, userId }),
    deny: (code: string) => api.post('/auth/pairings/deny', { code })
}

export const bridgeService = {
    list: () => api.get<BridgeSummary[]>('/bridges'),
    createLink: (bridge: string, name: string | null) => api.post<LinkCreated>('/links', { bridge, name })
}

export const apiKeyService = {
    list: () => api.get<ApiKeySummary[]>('/api-keys'),
    create: (name: string) => api.post<CreatedApiKey>('/api-keys', { name }),
    revoke: (id: string) => api.delete(`/api-keys/${id}`)
}

export const settingsService = {
    removal: () => api.get<ServerRemovalSettings>('/settings/removal'),
    saveRemoval: (settings: ServerRemovalSettings) => api.put<ServerRemovalSettings>('/settings/removal', settings)
}

export const auditService = {
    list: (offset: number, limit: number) => api.get<AuditPage>('/audit', { offset, limit })
}

export const subtitleService = {
    list: () => api.get<SubtitleRequest[]>('/subtitle-requests'),
    cancel: (id: string) => api.post<SubtitleRequest>(`/subtitle-requests/${id}/cancel`)
}

export const webhookService = {
    list: () => api.get<OutgoingWebhook[]>('/outgoing-webhooks'),
    create: (name: string, url: string, eventTypes: string[]) => api.post<CreatedOutgoingWebhook>('/outgoing-webhooks', { name, url, eventTypes }),
    update: (id: string, changes: { name?: string; url?: string; eventTypes?: string[]; enabled?: boolean }) =>
        api.patch<OutgoingWebhook>(`/outgoing-webhooks/${id}`, changes),
    remove: (id: string) => api.delete(`/outgoing-webhooks/${id}`),
    rotate: (id: string) => api.post<CreatedOutgoingWebhook>(`/outgoing-webhooks/${id}/rotate-secret`),
    test: (id: string) => api.post<OutgoingWebhookTestResult>(`/outgoing-webhooks/${id}/test`)
}

export const traktService = {
    status: () => api.get<TraktStatus>('/users/me/trakt'),
    link: () => api.post<TraktStatus>('/users/me/trakt/link'),
    poll: () => api.post<TraktStatus>('/users/me/trakt/link/poll'),
    importHistory: () => api.post<TraktImportResult>('/users/me/trakt/import'),
    unlink: () => api.delete('/users/me/trakt')
}
