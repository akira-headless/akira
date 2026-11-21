const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
const absolute = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

const steps: [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 365 * 24 * 3600],
    ['month', 30 * 24 * 3600],
    ['day', 24 * 3600],
    ['hour', 3600],
    ['minute', 60]
]

export function ago(timestamp: string | null): string {
    if (!timestamp) {
        return 'never'
    }

    const seconds = (new Date(timestamp).getTime() - Date.now()) / 1000
    for (const [unit, size] of steps) {
        if (Math.abs(seconds) >= size) {
            return relative.format(Math.round(seconds / size), unit)
        }
    }
    return 'just now'
}

export function when(timestamp: string | null): string {
    return timestamp ? absolute.format(new Date(timestamp)) : ''
}

export function words(camel: string): string {
    const spaced = camel.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
    return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}
