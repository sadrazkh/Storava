import type {
  AdvisorFinding,
  AdvisorPriority,
  AdvisorReviewTarget,
  AdvisorResponse,
  AdvisorResult,
  AdvisorSettings,
  SanitizedScanSummary,
} from '@/models/advisor';
import { advisorSignalIds } from '@/models/advisor';
import { normalizeAdvisorSettings, normalizeOpenRouterBaseUrl } from '@/services/advisorSettings';

type JsonRecord = Record<string, unknown>;

const advisorResponseSchema = {
  type: 'object',
  additionalProperties: false,
  properties: {
    title: { type: 'string', minLength: 1, maxLength: 120 },
    executiveSummary: { type: 'string', minLength: 1, maxLength: 1_500 },
    findings: {
      type: 'array',
      maxItems: 8,
      items: {
        type: 'object',
        additionalProperties: false,
        properties: {
          title: { type: 'string', minLength: 1, maxLength: 120 },
          evidence: { type: 'string', minLength: 1, maxLength: 600 },
          risk: { type: 'string', enum: ['low', 'medium', 'high'] },
          confidence: { type: 'number', minimum: 0, maximum: 1 },
        },
        required: ['title', 'evidence', 'risk', 'confidence'],
      },
    },
    priorities: {
      type: 'array',
      maxItems: 5,
      items: {
        type: 'object',
        additionalProperties: false,
        properties: {
          title: { type: 'string', minLength: 1, maxLength: 120 },
          reason: { type: 'string', minLength: 1, maxLength: 600 },
          confidence: { type: 'number', minimum: 0, maximum: 1 },
        },
        required: ['title', 'reason', 'confidence'],
      },
    },
    reviewTargets: {
      type: 'array',
      maxItems: 6,
      items: {
        type: 'object',
        additionalProperties: false,
        properties: {
          signal: { type: 'string', enum: advisorSignalIds },
          disposition: { type: 'string', enum: ['cleanup-candidate', 'archive-candidate', 'investigate'] },
          rationale: { type: 'string', minLength: 1, maxLength: 500 },
          confidence: { type: 'number', minimum: 0, maximum: 1 },
        },
        required: ['signal', 'disposition', 'rationale', 'confidence'],
      },
    },
    cautions: {
      type: 'array',
      maxItems: 6,
      items: { type: 'string', minLength: 1, maxLength: 400 },
    },
    disclaimer: { type: 'string', minLength: 1, maxLength: 600 },
    privacyNote: { type: 'string', minLength: 1, maxLength: 400 },
  },
  required: ['title', 'executiveSummary', 'findings', 'priorities', 'reviewTargets', 'cautions', 'disclaimer', 'privacyNote'],
} as const;

function isRecord(value: unknown): value is JsonRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function assertExactKeys(value: JsonRecord, keys: string[], label: string): void {
  const actual = Object.keys(value);
  if (actual.length !== keys.length || actual.some((key) => !keys.includes(key))) {
    throw new Error(`${label} contains unsupported fields.`);
  }
}

function requiredString(value: unknown, label: string, maximum: number): string {
  if (typeof value !== 'string' || value.trim().length === 0 || value.length > maximum) {
    throw new Error(`${label} is invalid.`);
  }
  return value.trim();
}

function confidence(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > 1) {
    throw new Error(`${label} is invalid.`);
  }
  return value;
}

function parseFinding(value: unknown): AdvisorFinding {
  if (!isRecord(value)) throw new Error('Advisor finding is invalid.');
  assertExactKeys(value, ['title', 'evidence', 'risk', 'confidence'], 'Advisor finding');
  if (value.risk !== 'low' && value.risk !== 'medium' && value.risk !== 'high') {
    throw new Error('Advisor finding risk is invalid.');
  }
  return {
    title: requiredString(value.title, 'Advisor finding title', 120),
    evidence: requiredString(value.evidence, 'Advisor finding evidence', 600),
    risk: value.risk,
    confidence: confidence(value.confidence, 'Advisor finding confidence'),
  };
}

function parsePriority(value: unknown): AdvisorPriority {
  if (!isRecord(value)) throw new Error('Advisor priority is invalid.');
  assertExactKeys(value, ['title', 'reason', 'confidence'], 'Advisor priority');
  return {
    title: requiredString(value.title, 'Advisor priority title', 120),
    reason: requiredString(value.reason, 'Advisor priority reason', 600),
    confidence: confidence(value.confidence, 'Advisor priority confidence'),
  };
}

function parseReviewTarget(value: unknown): AdvisorReviewTarget {
  if (!isRecord(value)) throw new Error('Advisor review target is invalid.');
  assertExactKeys(value, ['signal', 'disposition', 'rationale', 'confidence'], 'Advisor review target');
  if (typeof value.signal !== 'string' || !advisorSignalIds.some((signal) => signal === value.signal)) {
    throw new Error('Advisor review target signal is invalid.');
  }
  if (
    value.disposition !== 'cleanup-candidate'
    && value.disposition !== 'archive-candidate'
    && value.disposition !== 'investigate'
  ) {
    throw new Error('Advisor review target disposition is invalid.');
  }
  return {
    signal: value.signal as AdvisorReviewTarget['signal'],
    disposition: value.disposition,
    rationale: requiredString(value.rationale, 'Advisor review target rationale', 500),
    confidence: confidence(value.confidence, 'Advisor review target confidence'),
  };
}

function parseStringArray(value: unknown, label: string, maximumItems: number, maximumLength: number): string[] {
  if (!Array.isArray(value) || value.length > maximumItems) throw new Error(`${label} is invalid.`);
  return value.map((item) => requiredString(item, label, maximumLength));
}

export function parseAdvisorResponse(value: unknown): AdvisorResponse {
  if (!isRecord(value)) throw new Error('Advisor response is not an object.');
  assertExactKeys(
    value,
    ['title', 'executiveSummary', 'findings', 'priorities', 'reviewTargets', 'cautions', 'disclaimer', 'privacyNote'],
    'Advisor response',
  );
  if (!Array.isArray(value.findings) || value.findings.length > 8) throw new Error('Advisor findings are invalid.');
  if (!Array.isArray(value.priorities) || value.priorities.length > 5) throw new Error('Advisor priorities are invalid.');
  if (!Array.isArray(value.reviewTargets) || value.reviewTargets.length > 6) {
    throw new Error('Advisor review targets are invalid.');
  }
  return {
    title: requiredString(value.title, 'Advisor title', 120),
    executiveSummary: requiredString(value.executiveSummary, 'Advisor summary', 1_500),
    findings: value.findings.map(parseFinding),
    priorities: value.priorities.map(parsePriority),
    reviewTargets: value.reviewTargets.map(parseReviewTarget),
    cautions: parseStringArray(value.cautions, 'Advisor caution', 6, 400),
    disclaimer: requiredString(value.disclaimer, 'Advisor disclaimer', 600),
    privacyNote: requiredString(value.privacyNote, 'Advisor privacy note', 400),
  };
}

export function buildOpenRouterRequest(
  settings: AdvisorSettings,
  summary: SanitizedScanSummary,
): JsonRecord {
  const language = settings.preferredLanguage === 'fa-IR' ? 'Persian (fa-IR)' : 'English (en-US)';
  return {
    model: settings.model,
    temperature: settings.temperature,
    max_tokens: settings.maxTokens,
    stream: false,
    provider: {
      require_parameters: true,
      data_collection: 'deny',
      ...(settings.requireZeroDataRetention ? { zdr: true } : {}),
    },
    response_format: {
      type: 'json_schema',
      json_schema: {
        name: 'storava_storage_advice',
        strict: true,
        schema: advisorResponseSchema,
      },
    },
    messages: [
      {
        role: 'system',
        content: `You are Storava's read-only storage advisor. Reply in ${language}. Analyze only the supplied aggregate metadata. Never infer file contents, identities, personal paths, or secrets. Never claim to have inspected files. Do not issue delete, move, rename, execute, or cleanup commands. Priorities are review priorities for a human, not file-operation instructions. reviewTargets may reference only rule signals that are present in ruleMatches; they identify aggregate classes for local human review and never individual files. Use cleanup-candidate only when the aggregate evidence suggests likely regeneratable or redundant data. State uncertainty and use the required JSON schema.`,
      },
      {
        role: 'user',
        content: JSON.stringify({
          task: 'Explain storage pressure, notable aggregate signals, and safe human review priorities.',
          sanitizedScanSummary: summary,
        }),
      },
    ],
  };
}

interface OpenRouterProviderOptions {
  fetchImplementation?: typeof fetch;
}

export class OpenRouterAdvisorProvider {
  private readonly fetchImplementation: typeof fetch;

  public constructor(options: OpenRouterProviderOptions = {}) {
    this.fetchImplementation = options.fetchImplementation ?? globalThis.fetch.bind(globalThis);
  }

  public async analyze(
    apiKey: string,
    settingsValue: AdvisorSettings,
    summary: SanitizedScanSummary,
  ): Promise<AdvisorResult> {
    const trimmedKey = apiKey.trim();
    if (trimmedKey.length < 12) throw new Error('An OpenRouter API key is required.');
    const baseUrl = normalizeOpenRouterBaseUrl(settingsValue.baseUrl);
    const settings = normalizeAdvisorSettings(settingsValue, settingsValue.preferredLanguage);
    const controller = new AbortController();
    const timeout = globalThis.setTimeout(() => controller.abort(), settings.timeoutMs);

    try {
      const response = await this.fetchImplementation(`${baseUrl}/chat/completions`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${trimmedKey}`,
          'Content-Type': 'application/json',
          'X-OpenRouter-Title': 'Storava Web',
        },
        body: JSON.stringify(buildOpenRouterRequest(settings, summary)),
        cache: 'no-store',
        credentials: 'omit',
        referrerPolicy: 'no-referrer',
        signal: controller.signal,
      });
      if (!response.ok) throw new Error(`OpenRouter request failed with status ${response.status}.`);
      const payload = await response.json() as unknown;
      if (!isRecord(payload) || !Array.isArray(payload.choices) || !isRecord(payload.choices[0])) {
        throw new Error('OpenRouter returned an invalid completion envelope.');
      }
      const choice = payload.choices[0];
      if (!isRecord(choice.message) || typeof choice.message.content !== 'string') {
        throw new Error('OpenRouter returned an empty structured response.');
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(choice.message.content) as unknown;
      } catch {
        throw new Error('OpenRouter returned malformed structured JSON.');
      }
      const result = parseAdvisorResponse(parsed);
      return {
        ...result,
        model: typeof payload.model === 'string' ? payload.model.slice(0, 180) : settings.model,
        generatedAt: new Date().toISOString(),
      };
    } catch (error) {
      if (controller.signal.aborted) throw new Error('OpenRouter request timed out.', { cause: error });
      if (error instanceof Error) throw error;
      throw new Error('OpenRouter request failed.', { cause: error });
    } finally {
      globalThis.clearTimeout(timeout);
    }
  }
}
