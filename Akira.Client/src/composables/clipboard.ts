export type CopyResult = 'copied' | 'selected' | 'failed'

/**
 * Copies text. The clipboard API only exists on https and localhost, so on the household's plain http address the older
 * way does it; when neither works the text on screen is selected, to copy by hand.
 */
export async function copyText(text: string, shown?: Element | null): Promise<CopyResult> {
    if (navigator.clipboard) {
        try {
            await navigator.clipboard.writeText(text)
            return 'copied'
        } catch {
            // Refused, for example without focus: the older way may still work
        }
    }

    const area = document.createElement('textarea')
    area.value = text
    area.setAttribute('readonly', '')
    area.style.position = 'fixed'
    area.style.opacity = '0'
    document.body.appendChild(area)
    area.select()
    let done = false
    try {
        done = document.execCommand('copy')
    } catch {
        done = false
    } finally {
        area.remove()
    }
    if (done) {
        return 'copied'
    }

    if (shown) {
        const range = document.createRange()
        range.selectNodeContents(shown)
        const selection = window.getSelection()
        selection?.removeAllRanges()
        selection?.addRange(range)
        return 'selected'
    }
    return 'failed'
}

/** What a copy button says after a press. */
export function copyLabel(result: CopyResult | null): string {
    switch (result) {
        case 'copied':
            return 'Copied'
        case 'selected':
            return 'Selected, press Ctrl+C'
        case 'failed':
            return 'Copy by hand'
        default:
            return 'Copy'
    }
}
