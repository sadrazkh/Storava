import type { Locale } from '@/localization/messages';
import type { AdvisorResult, SanitizedScanSummary } from '@/models/advisor';

export interface AdvisorReportCopy {
  reportLabel: string;
  generatedWith: string;
  privacy: string;
  summary: string;
  findings: string;
  priorities: string;
  cautions: string;
  confidence: string;
  evidence: string;
  disclaimer: string;
  category: string;
  count: string;
  size: string;
}

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function createBaseName(result: AdvisorResult): string {
  return `storava-ai-report-${result.generatedAt.slice(0, 10)}`;
}

export function createAdvisorJsonReport(
  result: AdvisorResult,
  summary: SanitizedScanSummary,
  locale: Locale,
): { blob: Blob; fileName: string } {
  const content = {
    format: 'storava-ai-report',
    version: 1,
    language: locale,
    privacy: 'aggregate-metadata-only',
    generatedAt: result.generatedAt,
    model: result.model,
    sanitizedScanSummary: summary,
    advice: {
      title: result.title,
      executiveSummary: result.executiveSummary,
      findings: result.findings,
      priorities: result.priorities,
      cautions: result.cautions,
      disclaimer: result.disclaimer,
      privacyNote: result.privacyNote,
    },
  };
  return {
    blob: new Blob([JSON.stringify(content, null, 2)], { type: 'application/json;charset=utf-8' }),
    fileName: `${createBaseName(result)}.json`,
  };
}

export function createAdvisorHtmlReport(
  result: AdvisorResult,
  summary: SanitizedScanSummary,
  locale: Locale,
  copy: AdvisorReportCopy,
): { blob: Blob; fileName: string } {
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
  const isPersian = locale === 'fa-IR';
  const findings = result.findings.map((finding) => `<article class="finding" data-risk="${escapeHtml(finding.risk)}">
<header><span>${escapeHtml(finding.risk)}</span><strong>${escapeHtml(finding.title)}</strong><small>${escapeHtml(copy.confidence)} ${number.format(finding.confidence * 100)}%</small></header>
<p><b>${escapeHtml(copy.evidence)}:</b> ${escapeHtml(finding.evidence)}</p></article>`).join('');
  const priorities = result.priorities.map((priority, index) => `<li><span>${number.format(index + 1)}</span><div><strong>${escapeHtml(priority.title)}</strong><p>${escapeHtml(priority.reason)}</p><small>${escapeHtml(copy.confidence)} ${number.format(priority.confidence * 100)}%</small></div></li>`).join('');
  const cautions = result.cautions.map((caution) => `<li>${escapeHtml(caution)}</li>`).join('');
  const categoryRows = summary.categories.map((category) => `<tr><td>${escapeHtml(category.category)}</td><td>${number.format(category.count)}</td><td>${number.format(category.bytes / 1024 ** 3)} GB</td></tr>`).join('');
  const html = `<!doctype html>
<html lang="${locale}" dir="${isPersian ? 'rtl' : 'ltr'}"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>${escapeHtml(result.title)} · Storava</title>
<style>:root{color-scheme:light}*{box-sizing:border-box}body{max-width:1080px;margin:0 auto;padding:3rem 1.5rem;background:#f3f4ec;color:#071a1c;font-family:system-ui,sans-serif;line-height:1.75}header.hero{padding:2rem 0 2.5rem;border-bottom:4px solid #baf36b}.eyebrow{letter-spacing:.12em;text-transform:uppercase;color:#42605b}h1{font-size:clamp(2.4rem,7vw,5.5rem);line-height:1;margin:.35rem 0 1rem}h2{margin-top:2.5rem}.meta{display:flex;gap:1rem;flex-wrap:wrap;color:#52615e}.summary{font-size:1.18rem;max-width:78ch}.finding{background:white;border:1px solid #d6dbd1;border-inline-start:5px solid #e6b95c;padding:1rem 1.2rem;margin:.8rem 0}.finding[data-risk=high]{border-inline-start-color:#d56254}.finding[data-risk=low]{border-inline-start-color:#7dbb70}.finding header{display:grid;grid-template-columns:auto 1fr auto;gap:.8rem;align-items:center}.finding header span{text-transform:uppercase;font-size:.75rem}.finding small,.meta,footer{color:#64716e}.priorities{list-style:none;padding:0}.priorities li{display:grid;grid-template-columns:2.5rem 1fr;gap:1rem;margin:1rem 0}.priorities li>span{display:grid;place-items:center;width:2.5rem;height:2.5rem;background:#071a1c;color:#baf36b;border-radius:50%}.priorities p{margin:.2rem 0}table{width:100%;border-collapse:collapse;background:white}th,td{text-align:start;padding:.7rem;border-bottom:1px solid #d9ddd4}.notice{padding:1rem 1.2rem;border:1px solid #c9d4c2;background:#ecf6dc}footer{margin-top:3rem;padding-top:1.2rem;border-top:1px solid #cdd3c8}@media print{body{padding:0;background:white}.finding,table{break-inside:avoid}}</style></head>
<body><header class="hero"><span class="eyebrow">${escapeHtml(copy.reportLabel)}</span><h1>${escapeHtml(result.title)}</h1><p class="summary">${escapeHtml(result.executiveSummary)}</p><div class="meta"><span>${escapeHtml(copy.generatedWith)} ${escapeHtml(result.model)}</span><time>${escapeHtml(new Intl.DateTimeFormat(locale, { dateStyle: 'long', timeStyle: 'short' }).format(new Date(result.generatedAt)))}</time></div></header>
<p class="notice">${escapeHtml(copy.privacy)} ${escapeHtml(result.privacyNote)}</p>
<h2>${escapeHtml(copy.findings)}</h2>${findings || '<p>—</p>'}
<h2>${escapeHtml(copy.priorities)}</h2><ol class="priorities">${priorities || '<li>—</li>'}</ol>
<h2>${escapeHtml(copy.cautions)}</h2><ul>${cautions || '<li>—</li>'}</ul>
<h2>${escapeHtml(copy.summary)}</h2><table><thead><tr><th>${escapeHtml(copy.category)}</th><th>${escapeHtml(copy.count)}</th><th>${escapeHtml(copy.size)}</th></tr></thead><tbody>${categoryRows}</tbody></table>
<p class="notice"><strong>${escapeHtml(copy.disclaimer)}:</strong> ${escapeHtml(result.disclaimer)}</p>
<footer>Storava Web · ${escapeHtml(copy.reportLabel)}</footer></body></html>`;
  return {
    blob: new Blob([html], { type: 'text/html;charset=utf-8' }),
    fileName: `${createBaseName(result)}.html`,
  };
}
