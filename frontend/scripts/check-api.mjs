import { mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, relative } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const temporary = await mkdtemp(join(tmpdir(), 'cityportal-api-'));
try {
  const result = spawnSync(
    process.execPath,
    ['node_modules/ng-openapi-gen/lib/index.js', '--output', temporary],
    { stdio: 'inherit' },
  );
  if (result.status !== 0) throw new Error('API client generation failed.');
  const generated = new URL('../src/app/api/', import.meta.url);
  const expectedFiles = (await readdir(temporary, { recursive: true, withFileTypes: true }))
    .filter((entry) => entry.isFile())
    .map((entry) => relative(temporary, join(entry.parentPath, entry.name)))
    .sort();
  const actualFiles = (await readdir(generated, { recursive: true, withFileTypes: true }))
    .filter((entry) => entry.isFile())
    .map((entry) => relative(fileURLToPath(generated), join(entry.parentPath, entry.name)))
    .sort();
  if (JSON.stringify(expectedFiles) !== JSON.stringify(actualFiles))
    throw new Error('Generated API file list differs. Run npm run generate:api.');
  for (const file of expectedFiles) {
    const expected = await readFile(join(temporary, file), 'utf8');
    const actual = await readFile(new URL(file.replaceAll('\\', '/'), generated), 'utf8');
    if (expected.replaceAll('\r\n', '\n') !== actual.replaceAll('\r\n', '\n'))
      throw new Error(`Generated API client differs: ${file}. Run npm run generate:api.`);
  }
  console.log('Generated API client matches the committed OpenAPI contract.');
} finally {
  await rm(temporary, { recursive: true, force: true });
}
