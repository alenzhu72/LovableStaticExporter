// Optional browser QA: install playwright, or set PLAYWRIGHT_MODULE to its index.mjs.
import fs from 'node:fs/promises';
import path from 'node:path';
import http from 'node:http';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : 'playwright');
const root = path.resolve(process.argv[2]);
const screenshots = path.resolve(process.argv[3]);
await fs.mkdir(screenshots, { recursive: true });
const server = http.createServer(async (req, res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    let file = path.resolve(root, '.' + (/^\/(project|hydration)\//.test(pathname) ? pathname : '/hydration' + pathname));
    if (!file.startsWith(root + path.sep)) throw new Error('Bad path');
    if ((await fs.stat(file)).isDirectory()) file = path.join(file, 'index.html');
    res.setHeader('Content-Type', ({ '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.ico': 'image/x-icon' })[path.extname(file)] || 'application/octet-stream');
    res.end(await fs.readFile(file));
  } catch { res.writeHead(404); res.end('Not found'); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
let browser;
try {
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL || 'msedge', headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const origin = 'http://127.0.0.1:' + server.address().port;
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(origin + '/project/dist/index.html');
  await page.waitForTimeout(1200);
  assert.equal(await page.locator('#lse-news-section .lse-card').count(), 3);
  assert.equal(await page.locator('meta[name="keywords"]').getAttribute('content'), 'BriteCard, 出海广告卡, 行业资讯');
  await page.locator('#lse-news-section').screenshot({ path: path.join(screenshots, 'home-news.png') });
  await page.goto(origin + '/project/dist/news/index.html');
  assert.equal(await page.locator('.lse-card').count(), 3);
  const color = await page.locator('.lse-news').first().evaluate(e => getComputedStyle(e).backgroundColor);
  assert.ok(color !== 'rgb(255, 255, 255)' && color !== 'rgba(0, 0, 0, 0)', color);
  await page.screenshot({ path: path.join(screenshots, 'news-list.png'), fullPage: true });
  await page.getByRole('link', { name: '3', exact: true }).click();
  assert.equal(await page.locator('.lse-card').count(), 1);
  await page.locator('.lse-card a').click();
  assert.equal(await page.locator('.lse-article img').count(), 1);
  assert.ok((await page.locator('meta[name="keywords"]').getAttribute('content')).startsWith('测试新闻'));
  await page.screenshot({ path: path.join(screenshots, 'news-article.png'), fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto(origin + '/project/dist/news/index.html');
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: path.join(screenshots, 'news-mobile.png'), fullPage: true });
  if (await fs.stat(path.join(root, 'hydration', 'index.html')).catch(() => null)) {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(origin + '/');
    await page.waitForTimeout(2000);
    assert.equal(await page.locator('#lse-news-section').count(), 1);
    assert.equal(await page.locator('#lse-news-nav').count(), 1);
    assert.equal(await page.locator('meta[name="keywords"]').getAttribute('content'), 'BriteCard, 出海广告卡, 行业资讯');
    await page.locator('#lse-news-nav').click();
    await page.waitForURL('**/news/index.html');
  }
  assert.deepEqual(errors, []);
  console.log('PASS: browser homepage/news navigation, pagination, article image/SEO, inherited theme and mobile layout.');
} finally { if (browser) await browser.close(); await new Promise(resolve => server.close(resolve)); }
