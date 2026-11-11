import { writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';

const source = process.argv[2] ?? 'http://localhost:5080/openapi/v1.json';
const response = await fetch(source);
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
