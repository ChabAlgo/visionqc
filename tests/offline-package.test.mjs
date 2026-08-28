import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const web = read('visionqc-extension.js');
const html = read('index.html');
const server = read('LocalAgent_v0.2.12/AgentServer.cs');
const installer = read('LocalAgent_v0.2.12/OfflineInstaller/Program.cs');
const installerProject = read('LocalAgent_v0.2.12/OfflineInstaller/VisionQC.AgentInstaller.csproj');

test('download controls point to the versioned single-exe and offline package', () => {
  assert.match(web, /simulation-agent-download/);
  assert.match(web, /simulation-offline-download/);
  assert.match(web, /VisionQC_Agent_Installer_v1\.3\.1\.exe/);
  assert.match(web, /VisionQC_Offline_v4\.7\.9\.zip/);
  assert.match(web, /function downloadAgentInstaller/);
  assert.match(web, /function downloadOfflinePackage/);
});

test('offline entry UI has no CDN or remote stylesheet/script dependency', () => {
  assert.doesNotMatch(html, /https?:\/\//i);
  assert.match(html, /assets\/tailwind-offline\.css/);
  assert.match(html, /assets\/jszip\.min\.js/);
  assert.match(html, /assets\/index-v4\.4\.33\.js/);
  assert.match(html, /visionqc-v470\.css/);
  assert.match(read('assets/tailwind-offline.css'), /fonts\/inter-latin-400-normal\.woff2/);
});

test('agent securely serves the bundled offline UI from its launcher-owned home', () => {
  assert.match(server, /RunUntilExit\(bool openOfflinePage = false\)/);
  assert.match(server, /WriteOfflineWebAsset/);
  assert.match(server, /Program\.AgentHomeDirectory, "Web"/);
  assert.match(server, /Path\.GetFullPath/);
  assert.match(server, /OfflineContentType/);
  assert.match(server, /http:\/\/127\.0\.0\.1:/);
});

test('single installer embeds the launcher, all Worker APIs, local UI, and offline startup', () => {
  assert.match(installer, /LocalApplicationData/);
  assert.match(installer, /ExtractPayload/);
  assert.match(installer, /ExtractVpdlWorkerBundle/);
  assert.match(installer, /RunAndWait\(agentPath, "--register"/);
  assert.match(installer, /Arguments = "--offline"/);
  assert.match(installer, /StopRunningAgent/);
  assert.match(installer, /VisionQC\.VpdlWorker/);
  assert.match(installerProject, /Payload\.Launcher\.VisionQC\.LocalAgent\.exe/);
  assert.match(installerProject, /Payload\.WorkerManifest\.vpdl-workers\.json/);
  assert.match(installerProject, /Payload\.WorkerBundle\.vpdl-workers\.zip/);
  assert.match(installerProject, /Payload\.Web\.index\.html/);
  assert.match(installerProject, /Payload\.Web\.assets\.index-v4\.4\.33\.js/);
  assert.match(installerProject, /Payload\.Web\.visionqc-v470\.css/);
  assert.match(installerProject, /Payload\.Web\.assets\.fonts\.inter-latin-400-normal\.woff2/);
});
