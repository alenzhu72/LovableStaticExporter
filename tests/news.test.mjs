import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { seo, generateNews, loadSettings } from '../news.mjs';

async function fixture(t) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lse-news-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const stage = path.join(root, 'dist');
  await fs.mkdir(stage);
  await fs.writeFile(path.join(stage, 'index.html'), '<!doctype html><html><head><title>Test Site</title><meta name="keywords" content="old"><style>:root{--background:oklch(.2 .02 250);--primary:yellow}body{font-family:sans-serif}</style></head><body><header><nav></nav></header><main>Home</main></body></html>');
  return { root, stage };
}
const article = (i, extra = {}) => ({ title: `新闻 ${i}`, slug: `post-${i}`, date: `2026-10-${String(i).padStart(2, '0')}`, content: '第一段正文\n\n第二段正文', keywords: '本页关键词', ...extra });

test('SEO replaces duplicates, escapes HTML and preserves literal dollar patterns', () => {
  const html = seo('<head><title>old</title><meta content="a" name="keywords"><meta name="KEYWORDS" content="b"></head>', { title: '标题 $& <tag>', keywords: '词, "quoted", $&', description: '</head><script>alert(1)</script>' });
  assert.equal((html.match(/name="keywords"/g) || []).length, 1);
  assert.ok(html.includes('标题 $&amp; &lt;tag&gt;'));
  assert.ok(html.includes('词, &quot;quoted&quot;, $&amp;'));
  assert.ok(!html.includes('<script>'));
});
test('generates sorted multi-page list, single-image articles and working relative links', async t => {
  const { root, stage } = await fixture(t);
  await fs.writeFile(path.join(root, 'photo.png'), Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7O8AAAAASUVORK5CYII=', 'base64'));
  const settings = { newsEnabled: true, keywords: '全站', siteUrl: 'https://example.com', pageSize: 2, newsTitle: '行业动态 $&', articles: [article(1), article(2), article(3, { image: 'photo.png', title: '安全 <script> & "标题"' })] };
  await generateNews(root, stage, settings);
  const home = await fs.readFile(path.join(stage, 'index.html'), 'utf8');
  assert.ok(home.includes('name="keywords" content="全站"'));
  assert.ok(home.includes('行业动态 $&'));
  assert.ok(home.indexOf('post-3.html') < home.indexOf('post-1.html'));
  const listing = await fs.readFile(path.join(stage, 'news', 'index.html'), 'utf8');
  assert.ok(listing.includes('post-3.html') && listing.includes('post-2.html') && !listing.includes('post-1.html'));
  const page2 = await fs.readFile(path.join(stage, 'news', 'page-2.html'), 'utf8');
  assert.ok(page2.includes('post-1.html'));
  const detail = await fs.readFile(path.join(stage, 'news', 'post-3.html'), 'utf8');
  assert.ok(detail.includes('name="keywords" content="本页关键词"'));
  assert.ok(detail.includes('安全 &lt;script&gt; &amp; &quot;标题&quot;'));
  assert.equal((detail.match(/<img /g) || []).length, 1);
  assert.ok(detail.includes('<p>第一段正文</p><p>第二段正文</p>'));
  const structured = JSON.parse(detail.match(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/)[1]);
  assert.equal(structured.headline, settings.articles[2].title);
  assert.ok((await fs.readFile(path.join(stage, 'news-sitemap.xml'), 'utf8')).includes('https://example.com/news/page-2.html'));
  assert.ok(!settings.articles[2].imageUrl, 'does not mutate saved settings');
  for (const file of ['index.html', 'news/index.html', 'news/page-2.html', 'news/post-1.html', 'news/post-2.html', 'news/post-3.html']) {
    const html = await fs.readFile(path.join(stage, file), 'utf8');
    for (const m of html.matchAll(/(?:src|href)="([^"#]+)"/g)) {
      if (/^https?:/.test(m[1])) continue;
      await fs.access(path.resolve(path.dirname(path.join(stage, file)), m[1]));
    }
  }
});
test('disabled news only changes requested SEO; empty news has a usable list', async t => {
  const { root, stage } = await fixture(t);
  await generateNews(root, stage, { keywords: '关键词' });
  assert.equal(await fs.stat(path.join(stage, 'news')).catch(() => null), null);
  await generateNews(root, stage, { newsEnabled: true, articles: [] });
  assert.ok((await fs.readFile(path.join(stage, 'news/index.html'), 'utf8')).includes('暂无新闻'));
});
test('rejects duplicate/reserved/traversal slugs and invalid dates before generating pages', async t => {
  for (const items of [[article(1), article(1)], [article(1, { slug: '../escape' })], [article(1, { slug: 'index' })], [article(1, { slug: 'con' })], [article(1, { date: '2026-02-31' })]]) {
    const { root, stage } = await fixture(t);
    await assert.rejects(generateNews(root, stage, { newsEnabled: true, articles: items }));
  }
});
test('rejects invalid images, source news collisions and unsafe canonical URL', async t => {
  const { root, stage } = await fixture(t);
  await fs.writeFile(path.join(root, 'fake.png'), '<html>not an image</html>');
  await assert.rejects(generateNews(root, stage, { newsEnabled: true, articles: [article(1, { image: 'fake.png' })] }), /must be PNG/);
  await assert.rejects(generateNews(root, stage, { newsEnabled: true }), /already has a news directory/);
  await assert.rejects(generateNews(root, stage, { siteUrl: 'javascript:alert(1)' }), /Website URL/);
});
test('old HSL theme syntax is preserved and portable settings load with BOM', async t => {
  const { root, stage } = await fixture(t);
  await fs.writeFile(path.join(stage, 'index.html'), '<html><head><title>Site</title><style>:root{--background:222 47% 11%;--foreground:0 0% 100%}</style></head><body>Home</body></html>');
  await fs.mkdir(path.join(root, '.static-exporter'));
  await fs.writeFile(path.join(root, '.static-exporter/settings.json'), '\uFEFF' + JSON.stringify({ newsEnabled: true, articles: [] }));
  await generateNews(root, stage, await loadSettings(root));
  assert.ok((await fs.readFile(path.join(stage, 'news/index.html'), 'utf8')).includes('hsl(var(--background))'));
});
