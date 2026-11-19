import { writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';

const requested = new URL(process.argv[2] ?? 'http://localhost:5080/openapi/v1.json');
const hostname = ['localhost', '127.0.0.1', '[::1]'].find((host) => host === requested.hostname);
if (
  !hostname ||
  !['http:', 'https:'].includes(requested.protocol) ||
  requested.username ||
  requested.password ||
  requested.pathname !== '/openapi/v1.json' ||
  requested.search ||
  requested.hash
) {
  throw new Error('Use the local API URL ending in /openapi/v1.json.');
}
const protocol = requested.protocol === 'https:' ? 'https:' : 'http:';
const source = new URL(`${protocol}//${hostname}/openapi/v1.json`);
if (requested.port) source.port = String(Number(requested.port));
const response = await fetch(source, { redirect: 'error' });
if (!response.ok) throw new Error(`OpenAPI download failed: HTTP ${response.status}`);
const specification = await response.json();
// Keep the contract independent of a developer's host and use same-origin requests.
delete specification.servers;
await writeFile(
  new URL('../openapi.json', import.meta.url),
  `${JSON.stringify(specification, null, 2)}\n`,
);
const result = spawnSync(process.execPath, ['node_modules/ng-openapi-gen/lib/index.js'], {
  stdio: 'inherit',
});
process.exit(result.status ?? 1);
