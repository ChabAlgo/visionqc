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
const workerBuild = read('LocalAgent_v0.2.12/BUILD_VPDL_WORKERS.ps1');
const workerLocator = read('LocalAgent_v0.2.12/Services/VpdlWorkerLocator.cs');

test('worker recovery opens offline UI only once and preserves crash diagnostics', () => {
  const launcher = read('LocalAgent_v0.2.12/Launcher/Program.cs');
  const diagnostics = read('LocalAgent_v0.2.12/Services/AgentDiagnostics.cs');
  assert.match(launcher, /process\.Start\(\);[\s\S]*?offline = false;[\s\S]*?process\.WaitForExit\(\)/);
  assert.match(launcher, /RedirectStandardError = true/);
  assert.match(launcher, /RedirectStandardOutput = true/);
  assert.match(launcher, /AgentDiagnostics\.Write\("EXIT"/);
  assert.match(diagnostics, /last-operation\.txt/);
  assert.match(diagnostics, /MaxLogBytes/);
  assert.match(diagnostics, /UnhandledException/);
  assert.match(diagnostics, /--query-gpu=name,driver_version,memory.total/);
  assert.match(server, /last-simulation-request\.json/);
  const green = read('LocalAgent_v0.2.12/Engine/GreenOverlayProcessor.cs');
  assert.match(green, /AgentDiagnostics\.Operation\("Green process[\s\S]*?sample\.Process\(tool\)/);
  assert.match(green, /AgentDiagnostics\.Operation\("Green heatmap[\s\S]*?view\.HeatMap/);
});

test('selected VPDL native path is prepended even if already present later in PATH', () => {
  const program = read('LocalAgent_v0.2.12/Program.cs');
  assert.match(program, /prefix \+ ";" \+ currentPath, EnvironmentVariableTarget\.Process/);
  assert.doesNotMatch(program, /currentPath\.IndexOf\(nativeBin/);
});

test('download controls point to the versioned single-exe and offline package', () => {
  assert.match(web, /simulation-agent-download/);
  assert.match(web, /simulation-offline-download/);
  assert.match(web, /VisionQC_Agent_Installer_v1\.3\.12\.exe/);
  assert.match(web, /VisionQC_Offline_v4\.7\.22\.zip/);
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

test('offline package always contains a VPDL-free Core Worker and uses Universal Worker when VPDL exists', () => {
  assert.match(workerBuild, /strategy = 'core-or-exact-or-universal'/);
  assert.match(workerBuild, /Workers\/Core\/VisionQC\.CoreWorker\.exe/);
  assert.match(workerBuild, /Worker bundle에 Core\/VisionQC\.CoreWorker\.exe가 없습니다/);
  assert.match(workerBuild, /Workers\/Universal\/VisionQC\.VpdlWorker\.exe/);
  assert.match(workerBuild, /Worker bundle에 Universal\/VisionQC\.VpdlWorker\.exe가 없습니다/);
  assert.match(workerLocator, /UniversalDirectoryName = "Universal"/);
  assert.match(installerProject, /Payload\.WorkerBundle\.vpdl-workers\.zip/);
  assert.match(installer, /VpdlRuntimeCatalog\.Discover\(\)\.Count > 0/);
  assert.match(read('LocalAgent_v0.2.12/CoreWorker/VisionQC.CoreWorker.csproj'), /VisionQC\.CoreWorker/);
  assert.doesNotMatch(read('LocalAgent_v0.2.12/CoreWorker/VisionQC.CoreWorker.csproj'), /ViDi\.NET/);
});
