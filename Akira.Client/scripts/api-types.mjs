import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import openapiTS, { astToString, COMMENT_HEADER } from 'openapi-typescript'

const source = new URL('../src/types/openapi.json', import.meta.url)
const target = new URL('../src/types/openapi.ts', import.meta.url)
const generated = COMMENT_HEADER + astToString(await openapiTS(source))

if (process.argv.includes('--check')) {
    // A checkout on Windows can turn the line endings into CRLF
    const current = existsSync(target) ? readFileSync(target, 'utf8').replace(/\r\n/g, '\n') : null
    if (current !== generated) {
        console.error('src/types/openapi.ts is not what openapi-typescript makes from src/types/openapi.json: run npm run api-types')
        process.exit(1)
    }
} else {
    writeFileSync(target, generated)
}
