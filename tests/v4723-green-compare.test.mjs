import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const dir = 'LocalAgent_v0.2.12/';
test('baseline engine is byte-identical to supplied original sources', () => {
  for (const [name, hash] of Object.entries({
    'GreenOverlayProcessor.cs':'f362c0919f9eb85a33b2be24f392e5ae2b22a13eeee89b6c317970d2d2cc18ee',
    'Models.cs':'7d2577bb40b64d1ae4b5d897efc31ab5bab32c10f71486b3cc9ae25b5dd9767e'
  })) assert.equal(createHash('sha256').update(readFileSync(new URL('../' + dir + 'GreenBaseline/Original/' + name, import.meta.url))).digest('hex'), hash);
});
test('comparison never forces production to use diagnostic native search', () => {
  const runner=read(dir+'GreenRunner/Program.cs'), compare=read(dir+'GreenCompare/Program.cs');
  assert.match(runner,/pipeName != null && searchMode != "pinned"/);
  for(const id of ['A-baseline-original','B-baseline-pinned','C-current-original','D-current-pinned']) assert.ok(compare.includes(id));
  assert.match(compare,/TotalImages/); assert.match(compare,/WorkspaceUnchanged/);
  assert.match(compare,/child.Refresh\(\)/);
  assert.match(compare,/Elapsed.TotalSeconds > 180/);
  assert.doesNotMatch(compare,/EnvironmentVariableTarget\.(Machine|User)|Sqlite|SQLite|https:\/\//);
});
test('installer and multi-version build include comparison executables', () => {
  const build=read(dir+'BUILD_VPDL_WORKERS.ps1');
  assert.ok(build.includes("@('GreenBaseline', 'GreenCompare')"));
  assert.ok(build.includes("$diagnosticProject + '\\VisionQC.' + $diagnosticProject + '.csproj'"));
  assert.ok(read(dir+'Launcher/Program.cs').includes('--green-compare'));
  assert.ok(read(dir+'OfflineInstaller/Program.cs').includes('VisionQC Green 비교 진단'));
});
