import type { ProblemDetails } from '@/types/api'

export class ApiError extends Error {
    readonly status: number
    readonly problem: ProblemDetails

    constructor(status: number, problem: ProblemDetails) {
        super(problem.detail ?? problem.title ?? (status === 0 ? 'Akira could not be reached.' : `Akira answered ${status}.`))
        this.status = status
        this.problem = problem
    }
}

// A failure that says nothing about the request itself: no answer at all, too many requests, or a server or proxy in trouble
export function isTransient(error: unknown) {
    return error instanceof ApiError && (error.status === 0 || error.status === 429 || error.status >= 500)
}

type Listener = () => void
const unauthorizedListeners: Listener[] = []

export function onUnauthorized(listener: Listener) {
    unauthorizedListeners.push(listener)
}

type Query = Record<string, string | number | boolean | null | undefined>

interface RequestOptions {
    body?: unknown
    query?: Query
    // A credential for this one request, for signing in; everything else rides on the session cookie
    headers?: Record<string, string>
    // Outlives the page, for the last progress report when the tab closes
    keepalive?: boolean
}

export async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
    // A url Akira made itself already has the prefix
    const url = new URL(path.startsWith('/api/') ? path : `/api/v1${path}`, window.location.origin)
    for (const [key, value] of Object.entries(options.query ?? {})) {
        if (value !== null && value !== undefined && value !== '') {
            url.searchParams.set(key, String(value))
        }
    }

    // Akira refuses a cookie request that changes something without this header
    const headers: Record<string, string> = { Accept: 'application/json', 'X-Akira-Request': '1', ...options.headers }
    if (options.body !== undefined) {
        headers['Content-Type'] = 'application/json'
    }

    let response: Response
    let text: string
    try {
        response = await fetch(url, {
            method,
            headers,
            credentials: 'same-origin',
            keepalive: options.keepalive,
            body: options.body === undefined ? undefined : JSON.stringify(options.body)
        })
        text = await response.text()
    } catch {
        throw new ApiError(0, {})
    }

    // A proxy in front of Akira answers its own errors, or its sign in page, as HTML
    let data: unknown
    let readable = true
    try {
        data = text ? JSON.parse(text) : undefined
    } catch {
        readable = false
    }

    if (!response.ok) {
        const problem = data !== null && typeof data === 'object' ? (data as ProblemDetails) : {}
        if (response.status === 401 && !options.headers) {
            unauthorizedListeners.forEach((listener) => listener())
        }
        throw new ApiError(response.status, problem)
    }
    if (!readable) {
        throw new ApiError(response.status, { detail: 'The answer did not come from Akira.' })
    }

    return data as T
}

export const api = {
    get: <T>(path: string, query?: Query) => request<T>('GET', path, { query }),
    post: <T>(path: string, body: unknown = {}, headers?: Record<string, string>) => request<T>('POST', path, { body, headers }),
    put: <T>(path: string, body: unknown) => request<T>('PUT', path, { body }),
    patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, { body }),
    delete: <T = void>(path: string) => request<T>('DELETE', path)
}
