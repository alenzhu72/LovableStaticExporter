import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { generateNews, loadSettings } from './news.mjs';

const root = path.resolve(process.argv[2] || '.');
const token = Date.now().toString();
const tempName = `.static-export-${token}.config.mjs`;
const tempConfig = path.join(root, tempName);
const dist = path.join(root, 'dist');
const backup = path.join(root, `.dist-backup-${token}`);
const stage = path.join(root, `.static-export-${token}`);
const zip = path.join(root, 'dist.zip');
let moved = false, committed = false;
const exists = async p => !!(await fs.stat(p).catch(() => null));
function run(cmd, args, cwd = root) {
  return new Promise((resolve, reject) => {
    const child = spawn(cmd, args, { cwd, stdio: 'inherit', windowsHide: true });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolve() : reject(new Error(`${cmd} exited with code ${code}`)));
  });
}
async function files(dir) {
  const result = [];
  for (const e of await fs.readdir(dir, { withFileTypes: true })) {
    if (e.isSymbolicLink()) throw new Error(`Symbolic links are not supported: ${e.name}`);
    const p = path.join(dir, e.name);
    result.push(...(e.isDirectory() ? await files(p) : [p]));
  }
  return result;
}
async function main() {
  const settings = await loadSettings(root);
  const pkg = JSON.parse(await fs.readFile(path.join(root, 'package.json'), 'utf8'));
  const deps = { ...pkg.dependencies, ...pkg.devDependencies };
  if (!pkg.scripts?.build || !/\bvite\s+build\b/.test(pkg.scripts.build)) throw new Error('Only Vite build projects are supported. Expected a vite build script.');
  for (const name of ['dist', '.output']) {
    const stat = await fs.lstat(path.join(root, name)).catch(() => null);
    if (stat?.isSymbolicLink()) throw new Error(`${name} must not be a symbolic link.`);
  }
  console.log('Project: ' + root);
  const npmCli = path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js');
  if (!(await exists(npmCli))) throw new Error('Please install the official Node.js distribution (including npm).');
  if (!(await exists(path.join(root, 'node_modules')))) {
    console.log('Installing dependencies...');
    await run(process.execPath, [npmCli, await exists(path.join(root, 'package-lock.json')) ? 'ci' : 'install']);
  }
  const configNames = ['vite.config.ts', 'vite.config.mts', 'vite.config.js', 'vite.config.mjs'];
  const config = (await Promise.all(configNames.map(async n => await exists(path.join(root, n)) ? n : null))).find(Boolean);
  const isStart = !!deps['@tanstack/react-start'];
  let buildConfig;
  if (isStart) {
    if (!config) throw new Error('TanStack project has no supported Vite configuration.');
    let source = await fs.readFile(path.join(root, config), 'utf8');
    // Keep the original extension so Vite can parse TypeScript configuration.
    buildConfig = `.static-export-${token}.config${path.extname(config)}`;
    if (deps['@lovable.dev/vite-tanstack-config'] && /import\s*\{\s*defineConfig\s*\}\s*from\s*["']@lovable.dev\/vite-tanstack-config["']/.test(source)) {
      source = source.replace(/import\s*\{\s*defineConfig\s*\}\s*from\s*(["']@lovable.dev\/vite-tanstack-config["'])/, 'import { defineConfig as exportBaseConfig } from $1');
      source += '\nfunction defineConfig(options = {}) { return exportBaseConfig({...options, tanstackStart: {...options.tanstackStart, prerender: {...options.tanstackStart?.prerender, enabled:true, autoSubfolderIndex:true, failOnError:true}}}); }\n';
    } else {
      throw new Error('This TanStack configuration is not supported automatically. Expected Lovable defineConfig import. No source files were changed.');
    }
    await fs.writeFile(path.join(root, buildConfig), source);
  } else if (config) buildConfig = config;
  if (await exists(dist)) { await fs.rename(dist, backup); moved = true; }
  console.log('Building static pages...');
  const args = [npmCli, 'run', 'build'];
  if (buildConfig) args.push('--', '--config', buildConfig);
  await run(process.execPath, args);
  const output = isStart ? path.join(root, '.output', 'public') : dist;
  if (!(await exists(path.join(output, 'index.html')))) throw new Error('No index.html was generated. This project may require a server or a custom deployment adapter.');
  await fs.cp(output, stage, { recursive: true });
  let liveUrl;
  const readme = await fs.readFile(path.join(root, 'README.md'), 'utf8').catch(() => '');
  liveUrl = readme.match(/https:\/\/[a-zA-Z0-9-]+\.lovable\.app\b/)?.[0];
  for (const htmlFile of (await files(stage)).filter(p => p.endsWith('.html'))) {
    let html = await fs.readFile(htmlFile, 'utf8');
    // Inline local CSS so double-click previews retain the complete design.
    const tags = [...html.matchAll(/<link\b[^>]*>/gi)];
    for (const [tag] of tags) {
      if (!/rel=["']stylesheet["']/i.test(tag)) continue;
      const href = tag.match(/href=["']([^"']+)["']/i)?.[1];
      if (!href || /^https?:|^\/\//.test(href)) continue;
      const cssPath = path.resolve(href.startsWith('/') ? stage : path.dirname(htmlFile), href.replace(/^\//, '').split('?')[0]);
      if (!cssPath.startsWith(stage + path.sep)) throw new Error('Invalid CSS resource path.');
      let css = await fs.readFile(cssPath, 'utf8');
      // Keep asset URLs valid after moving CSS into the document.
      css = css.replace(/url\((['"]?)([^)'"\s]+)\1\)/g, (all, q, url) => {
        if (/^(data:|https?:|\/\/|#)/.test(url)) return all;
        const resource = url.startsWith('/') ? path.join(stage, url.slice(1)) : path.resolve(path.dirname(cssPath), url);
        return `url("${path.relative(path.dirname(htmlFile), resource).split(path.sep).join('/')}")`;
      });
      html = html.replace(tag, `<style>${css.replace(/<\/style/gi, '<\\/style')}</style>`);
    }
    for (const m of [...html.matchAll(/(?:src|href)=["'](\/__l5e\/assets-v1\/[^"']+)["']/g)]) {
      const relative = m[1];
      const target = path.resolve(stage, '.' + decodeURIComponent(relative));
      if (!target.startsWith(stage + path.sep)) throw new Error('Invalid asset path.');
      if (!(await exists(target))) {
        if (!liveUrl) throw new Error('Lovable asset requires a published lovable.app URL in README.md: ' + relative);
        console.log('Downloading project asset: ' + relative);
        const response = await fetch(liveUrl + relative, { signal: AbortSignal.timeout(30000) });
        if (!response.ok || !response.headers.get('content-type')?.startsWith('image/')) throw new Error('Unable to download image: ' + relative);
        await fs.mkdir(path.dirname(target), { recursive: true });
        await fs.writeFile(target, Buffer.from(await response.arrayBuffer()));
      }
    }
    html = html.replace(/((?:src|href)=["'])\/(?!\/)([^"']*)(["'])/g, (all, prefix, resource, quote) => {
      const target = path.resolve(stage, resource.split('?')[0]);
      if (!target.startsWith(stage + path.sep)) return all;
      return prefix + './' + path.relative(path.dirname(htmlFile), target).split(path.sep).join('/') + quote;
    });
    await fs.writeFile(htmlFile, html);
  }
  await fs.writeFile(path.join(stage, 'web.config'), '<?xml version="1.0" encoding="utf-8"?><configuration><system.webServer><defaultDocument enabled="true"><files><clear/><add value="index.html"/></files></defaultDocument></system.webServer></configuration>');
  await generateNews(root, stage, settings);
  // Vite has completed writing its build output. Replace only the generated dist.
  if (await exists(dist)) await fs.rm(dist, { recursive: true });
  await fs.rename(stage, dist);
  const tempZip = path.join(root, `.dist-${token}.zip`);
  const psQuote = p => "'" + p.replaceAll("'", "''") + "'";
  await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::CreateFromDirectory(${psQuote(dist)}, ${psQuote(tempZip)})`]);
  await fs.copyFile(tempZip, zip);
  await fs.unlink(tempZip);
  committed = true;
  console.log('\nSUCCESS\nFolder: ' + dist + '\nZIP: ' + zip + '\nUpload all dist contents to the IIS website root.');
}
try { await main(); }
catch (error) { console.error('\nEXPORT FAILED: ' + error.message); process.exitCode = 1; }
finally {
  for (const ext of ['.ts', '.mts', '.js', '.mjs']) await fs.rm(path.join(root, `.static-export-${token}.config${ext}`), { force: true }).catch(() => {});
  await fs.rm(tempConfig, { force: true }).catch(() => {});
  await fs.rm(stage, { recursive: true, force: true }).catch(() => {});
  if (moved && !committed) {
    await fs.rm(dist, { recursive: true, force: true });
    await fs.rename(backup, dist);
    console.log('Original dist restored.');
  }
  if (committed && moved) console.log('Previous dist preserved at: ' + backup);
}
