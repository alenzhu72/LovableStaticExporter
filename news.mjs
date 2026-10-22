import fs from 'node:fs/promises';
import path from 'node:path';

export const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const attr = (tag, name) => tag.match(new RegExp(`\\b${name}\\s*=\\s*["']([^"']*)["']`, 'i'))?.[1];
export function seo(html, { keywords, description, title, canonical } = {}) {
  if (!/<\/head\s*>/i.test(html)) throw new Error('HTML has no closing head element.');
  function meta(name, value) {
    if (!String(value ?? '').trim()) return;
    html = html.replace(/<meta\b[^>]*>/gi, tag => attr(tag, 'name')?.toLowerCase() === name ? '' : tag);
    html = html.replace(/<\/head\s*>/i, () => `<meta name="${name}" content="${escapeHtml(value)}"></head>`);
  }
  meta('keywords', keywords); meta('description', description);
  if (title) html = html.replace(/<title\b[^>]*>[\s\S]*?<\/title>/gi, '').replace(/<\/head\s*>/i, () => `<title>${escapeHtml(title)}</title></head>`);
  if (canonical) {
    html = html.replace(/<link\b[^>]*>/gi, tag => attr(tag, 'rel')?.toLowerCase() === 'canonical' ? '' : tag);
    html = html.replace(/<\/head\s*>/i, () => `<link rel="canonical" href="${escapeHtml(canonical)}"></head>`);
  }
  return html;
}
const css = `
.lse-news{font-family:inherit;color:var(--foreground,#17212f);background:var(--background,#fff);line-height:1.75;box-sizing:border-box}
.lse-news *{box-sizing:border-box}.lse-news a{color:inherit;text-decoration:none}.lse-news a:hover{text-decoration:underline}
.lse-wrap{max-width:1120px;margin:auto;padding:48px 24px}.lse-top{border-bottom:1px solid var(--border,#ddd)}.lse-top .lse-wrap{padding-top:20px;padding-bottom:20px;display:flex;justify-content:space-between;gap:24px;flex-wrap:wrap}
.lse-news h1{font-size:clamp(28px,5vw,44px);font-weight:700;line-height:1.25;margin:0 0 24px}.lse-news h2{font-size:24px;font-weight:700;margin:12px 0}.lse-news p{margin:0 0 20px}.lse-muted{color:var(--muted-foreground,#6b7280)}
.lse-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,280px),1fr));gap:24px}.lse-card{border:1px solid var(--border,#ddd);border-radius:20px;overflow:hidden;display:flex;flex-direction:column}.lse-card img{display:block;width:100%;aspect-ratio:16/9;object-fit:cover}.lse-card-body{padding:24px}.lse-card h2{font-size:22px}.lse-pill{display:inline-block;padding:10px 22px;border-radius:99px;background:var(--primary,#1967d2);color:var(--primary-foreground,#fff)!important;font-weight:600}
.lse-pages{display:flex;gap:12px;flex-wrap:wrap;margin-top:32px}.lse-pages a{padding:6px 14px;border:1px solid var(--border,#ddd);border-radius:8px}.lse-pages [aria-current]{background:var(--primary,#1967d2);color:var(--primary-foreground,#fff)}
.lse-article{max-width:820px}.lse-article img{display:block;width:100%;max-height:600px;object-fit:contain;border-radius:20px;margin:28px 0}.lse-prose{overflow-wrap:anywhere}.lse-prose p{white-space:pre-wrap}.lse-section-head{display:flex;justify-content:space-between;gap:20px;align-items:center;margin-bottom:28px;flex-wrap:wrap}
`;
async function allFiles(dir) {
  const result = [];
  for (const e of await fs.readdir(dir, { withFileTypes: true })) {
    if (e.isSymbolicLink()) throw new Error('News output must not contain symbolic links.');
    const p = path.join(dir, e.name);
    result.push(...(e.isDirectory() ? await allFiles(p) : [p]));
  }
  return result;
}
export async function loadSettings(root) {
  try { return JSON.parse((await fs.readFile(path.join(root, '.static-exporter', 'settings.json'), 'utf8')).replace(/^\uFEFF/, '')); }
  catch (e) { if (e.code === 'ENOENT') return {}; throw new Error('Cannot read SEO/news settings: ' + e.message); }
}
export async function generateNews(root, stage, settings = {}) {
  const siteBase = String(settings.siteUrl || '').trim().replace(/\/+$/, '');
  if (siteBase && (!/^https?:\/\//i.test(siteBase) || new URL(siteBase).search || new URL(siteBase).hash || new URL(siteBase).username || new URL(siteBase).password)) throw new Error('Website URL must be http(s), without credentials, query or fragment.');
  const homePath = path.join(stage, 'index.html');
  let home = await fs.readFile(homePath, 'utf8');
  for (const f of (await allFiles(stage)).filter(f => f.endsWith('.html'))) {
    const relative = path.relative(stage, f).split(path.sep).join('/');
    const html = seo(await fs.readFile(f, 'utf8'), { keywords: settings.keywords, description: f === homePath ? settings.description : undefined, canonical: siteBase ? siteBase + '/' + (relative === 'index.html' ? '' : relative) : undefined });
    await fs.writeFile(f, html);
  }
  home = await fs.readFile(homePath, 'utf8');
  if (!settings.newsEnabled) return;
  const newsDir = path.join(stage, 'news');
  if (await fs.stat(newsDir).catch(() => null)) throw new Error('The source website already has a news directory. Rename that route before enabling generated news.');
  const articles = Array.isArray(settings.articles) ? settings.articles.map(a => ({ ...a })) : [];
  const used = new Set(['index']);
  for (const a of articles) {
    if (!/^[a-z0-9][a-z0-9-]{0,79}$/.test(a.slug || '') || /^(?:page-\d+|con|prn|aux|nul|com[1-9]|lpt[1-9])$/.test(a.slug) || used.has(a.slug)) throw new Error('Article URL must be unique lowercase letters/numbers/hyphens (not index, page-N or Windows reserved names): ' + a.slug);
    if (!String(a.title || '').trim() || !String(a.content || '').trim()) throw new Error('Each article needs a title and body: ' + a.slug);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(a.date || '') || Number.isNaN(Date.parse(a.date)) || new Date(a.date).toISOString().slice(0, 10) !== a.date) throw new Error('Invalid article date: ' + a.slug);
    used.add(a.slug);
  }
  await fs.mkdir(path.join(newsDir, 'images'), { recursive: true });
  const name = String(settings.newsTitle || '新闻动态');
  const intro = String(settings.newsIntro || '了解我们的最新动态与行业资讯。');
  const sorted = [...articles].sort((a, b) => b.date.localeCompare(a.date));
  const pageSize = Math.min(50, Math.max(1, Number.parseInt(settings.pageSize, 10) || 6));
  const siteTitle = home.match(/<title[^>]*>([\s\S]*?)<\/title>/i)?.[1]?.replace(/&[^;]+;/g, ' ').trim() || '网站';
  let theme = [...home.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi)].map(m => m[1].replace(/url\((['"]?)([^)'"\s]+)\1\)/g, (full, q, url) => /^(data:|https?:|\/\/|#|\/)/.test(url) ? full : `url("../${url.replace(/^\.\//, '')}")`)).join('\n');
  // Older shadcn themes store bare HSL channels; newer themes use complete colors.
  const adaptedCss = css.replace(/var\(--(foreground|background|border|muted-foreground|primary|primary-foreground),([^\)]+)\)/g, (full, name) => {
    const value = theme.match(new RegExp(`--${name}\\s*:\\s*([^;}]+)`))?.[1]?.trim();
    return value && /^[\d.]+\s+[\d.]+%\s+[\d.]+%/.test(value) ? `hsl(var(--${name}))` : full;
  });
  const fonts = [...home.matchAll(/<link\b[^>]*>/gi)].map(m => m[0]).filter(tag => /^https:\/\/(fonts\.googleapis\.com|fonts\.gstatic\.com)(\/|$)/.test(attr(tag, 'href') || '')).join('');
  const htmlClass = escapeHtml(home.match(/<html\b[^>]*class=["']([^"']*)["']/i)?.[1] || '');
  const bodyClass = escapeHtml(home.match(/<body\b[^>]*class=["']([^"']*)["']/i)?.[1] || '');
  const shell = (title, body, keywords, description, filename) => seo(`<!doctype html><html lang="zh-CN" class="${htmlClass}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">${fonts}<style>${theme}\n${adaptedCss}</style></head><body class="${bodyClass}"><div class="lse-news"><header class="lse-top"><div class="lse-wrap"><a href="../index.html">${escapeHtml(siteTitle)}</a><nav><a href="index.html">${escapeHtml(name)}</a> · <a href="../index.html">返回首页</a></nav></div></header>${body}<footer class="lse-wrap lse-muted"><a href="index.html">${escapeHtml(name)}</a> · <a href="../index.html">返回首页</a></footer></div></body></html>`, { title, keywords, description, canonical: siteBase ? `${siteBase}/news/${filename}` : undefined });
  for (const a of sorted) {
    if (a.image) {
      const source = path.resolve(root, a.image);
      const info = await fs.lstat(source);
      if (!info.isFile() || info.isSymbolicLink()) throw new Error('Article image is not a regular file: ' + a.image);
      const bytes = await fs.readFile(source);
      let ext;
      if (bytes.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) ext = '.png';
      else if (bytes[0] === 255 && bytes[1] === 216 && bytes[2] === 255) ext = '.jpg';
      else if (bytes.toString('ascii', 0, 3) === 'GIF') ext = '.gif';
      else if (bytes.toString('ascii', 0, 4) === 'RIFF' && bytes.toString('ascii', 8, 12) === 'WEBP') ext = '.webp';
      else throw new Error('Article images must be PNG, JPEG, GIF or WebP: ' + a.image);
      if (bytes.length > 20 * 1024 * 1024) throw new Error('Article image exceeds 20 MB: ' + a.slug);
      a.imageUrl = `images/${a.slug}${ext}`;
      await fs.writeFile(path.join(newsDir, a.imageUrl), bytes);
    }
    const summary = String(a.summary || a.content.replace(/\s+/g, ' ').slice(0, 160));
    a.excerpt = summary;
    const body = `<main class="lse-wrap lse-article"><a href="index.html">← ${escapeHtml(name)}</a><h1>${escapeHtml(a.title)}</h1><p class="lse-muted"><time datetime="${escapeHtml(a.date)}">${escapeHtml(a.date)}</time></p>${a.imageUrl ? `<img src="${a.imageUrl}" alt="${escapeHtml(a.title)}">` : ''}<div class="lse-prose">${a.content.split(/\r?\n\s*\r?\n/).map(p => `<p>${escapeHtml(p)}</p>`).join('')}</div></main>`;
    let html = shell(`${a.title} | ${siteTitle}`, body, a.keywords || settings.keywords, summary, `${a.slug}.html`);
    const structured = { '@context': 'https://schema.org', '@type': 'NewsArticle', headline: a.title, datePublished: a.date, description: summary };
    if (siteBase) { structured.mainEntityOfPage = `${siteBase}/news/${a.slug}.html`; if (a.imageUrl) structured.image = `${siteBase}/news/${a.imageUrl}`; }
    html = html.replace('</head>', () => `<script type="application/ld+json">${JSON.stringify(structured).replace(/</g, '\\u003c')}</script></head>`);
    await fs.writeFile(path.join(newsDir, `${a.slug}.html`), html);
  }
  const cards = (items, prefix = '') => items.map(a => `<article class="lse-card"><a href="${prefix}${a.slug}.html">${a.imageUrl ? `<img src="${prefix}${a.imageUrl}" alt="${escapeHtml(a.title)}" loading="lazy">` : ''}<div class="lse-card-body"><time class="lse-muted">${escapeHtml(a.date)}</time><h2>${escapeHtml(a.title)}</h2><p class="lse-muted">${escapeHtml(a.excerpt)}</p><span>阅读全文 →</span></div></a></article>`).join('');
  const count = Math.max(1, Math.ceil(sorted.length / pageSize));
  const urls = [];
  for (let page = 1; page <= count; page++) {
    const filename = page === 1 ? 'index.html' : `page-${page}.html`;
    const pagination = count > 1 ? `<nav class="lse-pages" aria-label="新闻分页">${Array.from({ length: count }, (_, i) => `<a href="${i === 0 ? 'index.html' : `page-${i + 1}.html`}"${page === i + 1 ? ' aria-current="page"' : ''}>${i + 1}</a>`).join('')}</nav>` : '';
    const body = `<main class="lse-wrap"><h1>${escapeHtml(name)}</h1><p class="lse-muted">${escapeHtml(intro)}</p><div class="lse-grid">${cards(sorted.slice((page - 1) * pageSize, page * pageSize)) || '<p>暂无新闻，敬请关注。</p>'}</div>${pagination}</main>`;
    await fs.writeFile(path.join(newsDir, filename), shell(`${name}${page > 1 ? ' - 第' + page + '页' : ''} | ${siteTitle}`, body, settings.keywords, intro, filename));
    urls.push('news/' + filename);
  }
  const section = `<section id="lse-news-section" class="lse-news"><style>${adaptedCss}</style><div class="lse-wrap"><div class="lse-section-head"><div><h2>${escapeHtml(name)}</h2><p class="lse-muted">${escapeHtml(intro)}</p></div><a class="lse-pill" href="./news/index.html">查看全部 →</a></div><div class="lse-grid">${cards(sorted.slice(0, 3), './news/') || '<p>暂无新闻，敬请关注。</p>'}</div></div></section>`;
  // Keep content in the exported source/no-JS page, but detach it synchronously
  // before deferred React modules hydrate the document. Mount after the app loads.
  home = home.replace(/<\/body\s*>/i, () => section + '<script>document.getElementById("lse-news-section").remove();document.currentScript.remove();</script><script src="./news-home.js" defer></script></body>');
  const client = `(function(){if(document.currentScript)document.currentScript.remove();var markup=${JSON.stringify(section).replace(/</g, '\\u003c')};function ensure(){if(!document.body)return;if(!document.getElementById('lse-news-section'))document.body.insertAdjacentHTML('beforeend',markup);if(!document.getElementById('lse-news-nav')){var nav=document.querySelector('header nav');if(nav){var a=document.createElement('a');a.id='lse-news-nav';a.href='./news/index.html';a.textContent=${JSON.stringify(name).replace(/</g, '\\u003c')};nav.appendChild(a);}}}function start(){setTimeout(function(){ensure();new MutationObserver(ensure).observe(document.documentElement,{childList:true,subtree:true});},300);}if(document.readyState==='complete')start();else window.addEventListener('load',start,{once:true});})();`;
  await fs.writeFile(path.join(stage, 'news-home.js'), client);
  await fs.writeFile(homePath, home);
  if (siteBase) {
    const mapName = 'news-sitemap.xml';
    const paths = [...urls, ...sorted.map(a => `news/${a.slug}.html`)];
    await fs.writeFile(path.join(stage, mapName), `<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${paths.map(p => `<url><loc>${escapeHtml(siteBase + '/' + p)}</loc></url>`).join('')}</urlset>`);
    const robots = path.join(stage, 'robots.txt');
    const previous = await fs.readFile(robots, 'utf8').catch(e => { if (e.code === 'ENOENT') return ''; throw e; });
    await fs.writeFile(robots, previous + `\nSitemap: ${siteBase}/${mapName}\n`);
  }
  console.log(`SEO/news: ${sorted.length} articles, ${count} list pages.`);
}
