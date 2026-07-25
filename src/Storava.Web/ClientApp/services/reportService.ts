import type { Locale } from '@/localization/messages';
import type { ScanSession } from '@/models/scan';

export interface ReportCopy {
  kicker: string;
  privacy: string;
  size: string;
  files: string;
  folders: string;
  categories: string;
  category: string;
  count: string;
  largest: string;
  relativePath: string;
  risk: string;
}

function escapeHtml(value: string): string {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
}

export function createOfflineReport(session: ScanSession, locale: Locale, copy: ReportCopy): { blob: Blob; fileName: string } {
  const isPersian = locale === 'fa-IR';
  const number = new Intl.NumberFormat(locale);
  const bytes = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(session.metrics.bytes / 1024 ** 3);
  const categories = session.categories.map((category) =>
    `<tr><td>${escapeHtml(category.category)}</td><td>${number.format(category.count)}</td><td>${number.format(category.bytes / 1024 ** 2)} MB</td></tr>`).join('');
  const topItems = session.topItems.slice(0, 20).map((item) =>
    `<tr><td>${escapeHtml(item.relativePath)}</td><td>${number.format(item.size / 1024 ** 2)} MB</td><td>${escapeHtml(item.risk)}</td></tr>`).join('');
  const html = `<!doctype html>
<html lang="${locale}" dir="${isPersian ? 'rtl' : 'ltr'}"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>${escapeHtml(session.rootName)} · Storava</title>
<style>body{max-width:1050px;margin:3rem auto;padding:0 1.5rem;background:#f4f5ed;color:#071a1c;font-family:system-ui,sans-serif;line-height:1.7}header{border-bottom:3px solid #baf36b;padding-bottom:2rem}h1{font-size:clamp(2.5rem,7vw,5rem);line-height:1;margin:.2rem 0}small,p{color:#61706d}.metrics{display:grid;grid-template-columns:repeat(3,1fr);gap:1px;background:#cad0c6;border:1px solid #cad0c6;margin:2rem 0}.metrics div{background:white;padding:1rem}.metrics strong{display:block;font-size:1.5rem}table{width:100%;border-collapse:collapse;background:white;margin:1rem 0 2rem}th,td{padding:.7rem;text-align:start;border-bottom:1px solid #dde1d8}footer{margin-top:3rem;padding-top:1rem;border-top:1px solid #cad0c6}@media print{body{margin:0}.metrics div,table{break-inside:avoid}}</style></head>
<body><header><small>${escapeHtml(copy.kicker)}</small><h1>${escapeHtml(session.rootName)}</h1><p>${escapeHtml(copy.privacy)}</p></header>
<section class="metrics"><div><span>${escapeHtml(copy.size)}</span><strong>${bytes} GB</strong></div><div><span>${escapeHtml(copy.files)}</span><strong>${number.format(session.metrics.files)}</strong></div><div><span>${escapeHtml(copy.folders)}</span><strong>${number.format(session.metrics.folders)}</strong></div></section>
<h2>${escapeHtml(copy.categories)}</h2><table><thead><tr><th>${escapeHtml(copy.category)}</th><th>${escapeHtml(copy.count)}</th><th>${escapeHtml(copy.size)}</th></tr></thead><tbody>${categories}</tbody></table>
<h2>${escapeHtml(copy.largest)}</h2><table><thead><tr><th>${escapeHtml(copy.relativePath)}</th><th>${escapeHtml(copy.size)}</th><th>${escapeHtml(copy.risk)}</th></tr></thead><tbody>${topItems}</tbody></table>
<footer>Storava Web · ${new Intl.DateTimeFormat(locale, { dateStyle: 'long', timeStyle: 'short' }).format(session.createdAt)}</footer></body></html>`;
  const safeName = session.rootName.replace(/[^\p{L}\p{N}._-]+/gu, '-').slice(0, 80) || 'scan';
  return { blob: new Blob([html], { type: 'text/html;charset=utf-8' }), fileName: `${safeName}-report.html` };
}
