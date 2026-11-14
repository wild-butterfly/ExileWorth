/* global fetch, AbortSignal */

import { Buffer } from 'node:buffer';

import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFile, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { build } from 'esbuild';
import { fileURLToPath, URL } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const nodeVersion = '22.23.4';
const nodeArchive = `node-v${nodeVersion}-win-x64`;
const checksum = 'cbc74b5e0982c9dbb41525fd8fb5dcd32fbee237863f7829072f4639a1ce5f20';
const scratch = await mkdtemp(join(tmpdir(), 'vaaluation-windows-'));
const folder = 'Vaaluation-Windows-x64';
const bundle = join(scratch, folder);

function run(command, args, cwd = root) {
  const result = spawnSync(command, args, { cwd, stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(`${command} exited with status ${result.status}`);
}

try {
  await build({
    absWorkingDir: root,
    entryPoints: ['apps/windows/src/worker.ts'],
    bundle: true,
    platform: 'node',
    target: 'node22',
    outfile: 'apps/windows/dist/worker.cjs',
  });
  run('dotnet', [
    'publish',
    'apps/windows/native/Vaaluation.Windows.csproj',
    '-c',
    'Release',
    '-r',
    'win-x64',
    '--self-contained',
    'true',
    '-p:DebugType=None',
    '-o',
    bundle,
  ]);
  const response = await fetch(
    `https://nodejs.org/dist/v${nodeVersion}/${nodeArchive}.zip`,
    {
      signal: AbortSignal.timeout(90_000),
    },
  );
  if (!response.ok) throw new Error(`Node download returned ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  if (createHash('sha256').update(bytes).digest('hex') !== checksum) {
    throw new Error('The Node download did not match the pinned SHA-256 checksum.');
  }
  const nodeZip = join(scratch, 'node.zip');
  await writeFile(nodeZip, bytes);
  run(
    'tar',
    ['-xf', nodeZip, `${nodeArchive}/node.exe`, `${nodeArchive}/LICENSE`],
    scratch,
  );
  await copyFile(join(scratch, nodeArchive, 'node.exe'), join(bundle, 'node.exe'));
  await copyFile(join(scratch, nodeArchive, 'LICENSE'), join(bundle, 'NODE-LICENSE.txt'));
  await copyFile(join(root, 'apps/windows/dist/worker.cjs'), join(bundle, 'worker.cjs'));
  await copyFile(join(root, 'apps/windows/README.md'), join(bundle, 'README.md'));
  await copyFile(join(root, 'LICENSE'), join(bundle, 'LICENSE.txt'));
  const output = join(root, 'dist');
  await mkdir(output, { recursive: true });
  run('tar', ['-a', '-cf', join(output, `${folder}.zip`), folder], scratch);
} finally {
  await rm(scratch, { recursive: true, force: true });
}
