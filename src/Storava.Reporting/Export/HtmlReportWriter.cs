using System.Globalization;
using System.Net;
using System.Text;
using Storava.Domain.ValueObjects;
using Storava.Reporting.Model;

namespace Storava.Reporting.Export;

/// <summary>
/// Renders a self-contained HTML report: no external fonts, scripts or styles, so it opens
/// identically offline and can be archived or printed as-is. Persian reports are laid out
/// right-to-left.
/// </summary>
public sealed class HtmlReportWriter
{
    public string Write(StorageReport report, CultureInfo culture, IReportStrings strings)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(strings);
        culture ??= CultureInfo.InvariantCulture;

        bool rtl = culture.TextInfo.IsRightToLeft;
        var builder = new StringBuilder();

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine($"<html lang=\"{report.Language}\" dir=\"{(rtl ? "rtl" : "ltr")}\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"<title>{Encode(strings.ReportTitle)} — Storava</title>");
        builder.AppendLine(BuildStyles(rtl));
        builder.AppendLine("</head><body>");

        WriteHeader(builder, report, culture, strings);
        WriteMetrics(builder, report, culture, strings);
        WriteCategories(builder, report, culture, strings);
        WriteAiSection(builder, report, culture, strings);
        WriteRecommendations(builder, report, culture, strings);
        WriteLargest(builder, report, culture, strings);
        WriteFooter(builder, strings);

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static void WriteHeader(StringBuilder b, StorageReport report, CultureInfo culture, IReportStrings s)
    {
        b.AppendLine("<header class=\"hero\">");
        b.AppendLine("<div class=\"brand\"><span class=\"dot\"></span>Storava</div>");
        b.AppendLine($"<h1>{Encode(s.ReportTitle)}</h1>");
        b.AppendLine($"<p class=\"subtitle\">{Encode(report.RootPath)}</p>");
        b.AppendLine($"<p class=\"meta\">{Encode(s.GeneratedAt)}: {Encode(report.GeneratedAt.ToString("yyyy-MM-dd HH:mm", culture))}</p>");
        b.AppendLine("</header>");
    }

    private static void WriteMetrics(StringBuilder b, StorageReport report, CultureInfo culture, IReportStrings s)
    {
        b.AppendLine("<section class=\"metrics\">");
        AppendMetric(b, Size(report.TotalSize, culture), s.ScannedSize);
        AppendMetric(b, report.FileCount.ToString("N0", culture), s.Files);
        AppendMetric(b, report.FolderCount.ToString("N0", culture), s.Folders);
        AppendMetric(b, Size(report.TotalReclaimable, culture), s.Reclaimable, accent: true);
        if (report.ErrorCount > 0)
            AppendMetric(b, report.ErrorCount.ToString("N0", culture), s.SkippedErrors);
        b.AppendLine("</section>");
    }

    private static void AppendMetric(StringBuilder b, string value, string label, bool accent = false)
    {
        b.AppendLine($"<div class=\"metric{(accent ? " accent" : string.Empty)}\">");
        b.AppendLine($"<div class=\"value\">{Encode(value)}</div>");
        b.AppendLine($"<div class=\"label\">{Encode(label)}</div>");
        b.AppendLine("</div>");
    }

    private static void WriteCategories(StringBuilder b, StorageReport report, CultureInfo culture, IReportStrings s)
    {
        if (report.Categories.Count == 0)
            return;

        b.AppendLine($"<section class=\"card\"><h2>{Encode(s.ByCategory)}</h2>");
        foreach (var category in report.Categories)
        {
            int percent = (int)Math.Round(category.Share * 100);
            b.AppendLine("<div class=\"bar-row\">");
            b.AppendLine($"<div class=\"bar-label\">{Encode(category.Label)}</div>");
            b.AppendLine("<div class=\"bar-track\">");
            b.AppendLine($"<div class=\"bar-fill\" style=\"width:{percent}%\"></div>");
            b.AppendLine("</div>");
            b.AppendLine($"<div class=\"bar-value\">{Encode(Size(category.TotalSize, culture))} · {percent}%</div>");
            b.AppendLine("</div>");
        }
        b.AppendLine("</section>");
    }

    private static void WriteAiSection(StringBuilder b, StorageReport report, CultureInfo culture, IReportStrings s)
    {
        if (report.Ai is not { } ai)
            return;

        b.AppendLine($"<section class=\"card ai\"><h2>{Encode(s.AiAnalysis)}</h2>");
        b.AppendLine($"<p class=\"meta\">{Encode(ai.ModelName)} · {Encode(ai.GeneratedAt.ToString("yyyy-MM-dd HH:mm", culture))}</p>");

        if (!string.IsNullOrWhiteSpace(ai.Summary))
            b.AppendLine($"<p class=\"lead\">{Encode(ai.Summary!)}</p>");

        if (!string.IsNullOrWhiteSpace(ai.MainCause))
            b.AppendLine($"<p><strong>{Encode(s.MainCause)}:</strong> {Encode(ai.MainCause!)}</p>");

        if (!string.IsNullOrWhiteSpace(ai.Overview))
            b.AppendLine($"<p>{Encode(ai.Overview!)}</p>");

        AppendList(b, s.Findings, ai.Findings);
        AppendList(b, s.NextSteps, ai.NextSteps);

        if (ai.RejectedCount > 0)
        {
            b.AppendLine($"<p class=\"note\">{Encode(string.Format(culture, s.AiRejectedNote, ai.RejectedCount))}</p>");
        }

        b.AppendLine("</section>");
    }

    private static void AppendList(StringBuilder b, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;

        b.AppendLine($"<h3>{Encode(heading)}</h3><ul>");
        foreach (var item in items)
            b.AppendLine($"<li>{Encode(item)}</li>");
        b.AppendLine("</ul>");
    }

    private static void WriteRecommendations(StringBuilder b, StorageReport report, CultureInfo culture, IReportStrings s)
    {
        b.AppendLine($"<section class=\"card\"><h2>{Encode(s.Recommendations)}</h2>");

        if (report.Recommendations.Count == 0)
        {
            b.AppendLine($"<p class=\"empty\">{Encode(s.NoRecommendations)}</p></section>");
            return;
        }

        foreach (var item in report.Recommendations)
        {
            b.AppendLine("<article class=\"rec\">");
            b.AppendLine("<div class=\"rec-head\">");
            b.AppendLine($"<h3>{Encode(item.Title)}</h3>");
            b.AppendLine($"<span class=\"badge risk-{item.RiskLevel.ToString().ToLowerInvariant()}\">{Encode(item.RiskLabel)}</span>");
            b.AppendLine($"<span class=\"badge muted\">{Encode(item.CategoryLabel)}</span>");
            b.AppendLine($"<span class=\"rec-size\">{Encode(Size(item.EstimatedSpace, culture))}</span>");
            b.AppendLine("</div>");
            b.AppendLine($"<p class=\"path\">{Encode(item.Path)}</p>");
            b.AppendLine($"<p>{Encode(item.Reason)}</p>");

            if (!string.IsNullOrWhiteSpace(item.OfficialMigrationHint))
                b.AppendLine($"<p class=\"hint\"><strong>{Encode(s.OfficialMethod)}:</strong> {Encode(item.OfficialMigrationHint!)}</p>");

            if (!string.IsNullOrWhiteSpace(item.Warning))
                b.AppendLine($"<p class=\"warn\">{Encode(item.Warning!)}</p>");

            b.AppendLine("</article>");
        }

        b.AppendLine("</section>");
    }

    private static void WriteLargest(StringBuilder b, StorageReport report, CultureInfo culture, IReportStrings s)
    {
        if (report.LargestItems.Count == 0)
            return;

        b.AppendLine($"<section class=\"card\"><h2>{Encode(s.LargestItems)}</h2>");
        b.AppendLine($"<table><thead><tr><th>{Encode(s.Name)}</th><th>{Encode(s.Category)}</th><th class=\"num\">{Encode(s.Size)}</th></tr></thead><tbody>");

        foreach (var item in report.LargestItems)
        {
            b.AppendLine("<tr>");
            b.AppendLine($"<td><div class=\"cell-name\">{Encode(item.Name)}</div><div class=\"cell-path\">{Encode(item.Path)}</div></td>");
            b.AppendLine($"<td>{Encode(item.CategoryLabel)}</td>");
            b.AppendLine($"<td class=\"num\">{Encode(Size(item.Size, culture))}</td>");
            b.AppendLine("</tr>");
        }

        b.AppendLine("</tbody></table></section>");
    }

    private static void WriteFooter(StringBuilder b, IReportStrings s)
    {
        b.AppendLine($"<footer><p>{Encode(s.SafetyNote)}</p></footer>");
    }

    private static string Size(long bytes, CultureInfo culture) =>
        new ByteSize(Math.Max(0, bytes)).Humanize(culture);

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string BuildStyles(bool rtl)
    {
        string fontStack = rtl
            ? "'Vazirmatn', 'Segoe UI', Tahoma, sans-serif"
            : "'Inter', 'Segoe UI', Arial, sans-serif";
        string startEdge = rtl ? "right" : "left";

        return $$"""
            <style>
              :root {
                --bg: #0f172a; --surface: #1e293b; --surface-2: #273449;
                --text: #e2e8f0; --muted: #94a3b8; --accent: #0FB5AE;
                --low: #22C55E; --medium: #F59E0B; --high: #EF4444; --protected: #64748B;
                --radius: 14px;
              }
              * { box-sizing: border-box; }
              body {
                margin: 0; padding: 32px; background: var(--bg); color: var(--text);
                font-family: {{fontStack}}; line-height: 1.65; font-size: 15px;
              }
              .hero {
                background: linear-gradient(135deg, #0FB5AE 0%, #0E7490 55%, #1E293B 100%);
                border-radius: var(--radius); padding: 32px; margin-bottom: 24px;
              }
              .brand { font-weight: 700; letter-spacing: .4px; opacity: .95; }
              .dot {
                display: inline-block; width: 10px; height: 10px; border-radius: 50%;
                background: #fff; margin-inline-end: 8px;
              }
              h1 { margin: 12px 0 4px; font-size: 30px; }
              h2 { margin: 0 0 16px; font-size: 19px; }
              h3 { margin: 18px 0 8px; font-size: 16px; }
              .subtitle { margin: 0; opacity: .92; word-break: break-all; }
              .meta { color: var(--muted); font-size: 13px; margin: 6px 0 0; }
              .hero .meta { color: rgba(255,255,255,.85); }
              .metrics { display: flex; flex-wrap: wrap; gap: 12px; margin-bottom: 24px; }
              .metric {
                background: var(--surface); border-radius: var(--radius);
                padding: 18px 22px; min-width: 150px; flex: 1;
              }
              .metric .value { font-size: 24px; font-weight: 700; }
              .metric .label { color: var(--muted); font-size: 13px; }
              .metric.accent .value { color: var(--accent); }
              .card {
                background: var(--surface); border-radius: var(--radius);
                padding: 24px; margin-bottom: 20px;
              }
              .lead { font-size: 16px; }
              .bar-row { display: flex; align-items: center; gap: 12px; margin-bottom: 10px; }
              .bar-label { width: 170px; flex-shrink: 0; }
              .bar-track { flex: 1; height: 8px; background: var(--surface-2); border-radius: 999px; overflow: hidden; }
              .bar-fill { height: 100%; background: var(--accent); border-radius: 999px; }
              .bar-value { width: 150px; text-align: {{startEdge}}; color: var(--muted); font-size: 13px; }
              .rec {
                border-{{startEdge}}: 3px solid var(--accent); background: var(--surface-2);
                border-radius: 10px; padding: 16px 18px; margin-bottom: 14px;
              }
              .rec-head { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
              .rec-head h3 { margin: 0; flex: 1; min-width: 220px; }
              .rec-size { font-weight: 700; color: var(--accent); }
              .badge {
                font-size: 11px; font-weight: 600; padding: 3px 10px;
                border-radius: 999px; color: #fff;
              }
              .badge.muted { background: var(--protected); }
              .risk-low { background: var(--low); }
              .risk-medium { background: var(--medium); }
              .risk-high { background: var(--high); }
              .risk-protected { background: var(--protected); }
              .path, .cell-path {
                color: var(--muted); font-size: 12px; word-break: break-all;
                font-family: Consolas, 'Courier New', monospace; direction: ltr;
                text-align: {{startEdge}};
              }
              .hint { background: rgba(15,181,174,.12); border-radius: 8px; padding: 10px 12px; font-size: 13px; }
              .warn { background: rgba(245,158,11,.14); border-radius: 8px; padding: 10px 12px; font-size: 13px; }
              .note, .empty { color: var(--muted); font-size: 13px; }
              table { width: 100%; border-collapse: collapse; }
              th, td { padding: 10px 12px; border-bottom: 1px solid var(--surface-2); text-align: {{startEdge}}; }
              th { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .5px; }
              td.num, th.num { text-align: {{(rtl ? "left" : "right")}}; white-space: nowrap; }
              .cell-name { font-weight: 600; }
              ul { margin: 8px 0; padding-inline-start: 22px; }
              li { margin-bottom: 6px; }
              footer { color: var(--muted); font-size: 13px; text-align: center; padding: 16px 0; }
              @media print {
                body { background: #fff; color: #111; padding: 0; }
                .card, .metric { background: #fff; border: 1px solid #ddd; }
                .hero { background: #0E7490 !important; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
                .rec { background: #fafafa; }
              }
            </style>
            """;
    }
}
