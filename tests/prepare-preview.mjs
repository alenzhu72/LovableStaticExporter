// Prepare a copy of an existing exported homepage for framework hydration QA.
import fs from 'node:fs/promises';
import path from 'node:path';
import { generateNews } from '../news.mjs';
const source = path.resolve(process.argv[2]);
const project = path.resolve(process.argv[3]);
const output = path.resolve(process.argv[4]);
await fs.cp(source, output, { recursive: true });
const settings = JSON.parse((await fs.readFile(path.join(project, '.static-exporter/settings.json'), 'utf8')).replace(/^\uFEFF/, ''));
await generateNews(project, output, settings);
