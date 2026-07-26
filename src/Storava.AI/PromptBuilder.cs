using System.Text.Json;
using Storava.Contracts.Ai;

namespace Storava.AI;

/// <summary>
/// Builds the instructions sent alongside the payload. The prompt states plainly that Storava
/// executes nothing on the model's behalf and that suggestions must reference the supplied ids —
/// but the real guarantee is the validator, not the wording.
/// </summary>
public static class PromptBuilder
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="includeReport">
    /// When false the narrative report section is neither asked for nor described, so a user who
    /// turns it off does not pay for tokens they will never see.
    /// </param>
    public static string BuildSystemPrompt(string language, bool includeReport = true)
    {
        string languageName = language.StartsWith("fa", StringComparison.OrdinalIgnoreCase)
            ? "Persian (فارسی)"
            : "English";

        string reportRule = includeReport
            ? "- \"report\" holds a short narrative: an overview, the findings and the next steps."
            : "- Do not include a \"report\" object. Only the fields shown below are wanted.";

        string reportShape = includeReport
            ? ",\n  \"report\": {\n    \"overview\": \"...\",\n    \"findings\": [\"...\"],\n    \"nextSteps\": [\"...\"]\n  }"
            : string.Empty;

        // Two '$' so the JSON example's braces need no escaping; interpolation uses {{ }}.
        return $$"""
            You are the storage advisor inside Storava, a Windows disk-analysis tool.

            You are given an anonymised summary of one scan. Paths are already sanitized:
            placeholders such as <UserProfile> and <PrivateFolder-3> stand in for real names.

            Rules you must follow:
            - Reply with a single JSON object and nothing else. No markdown, no code fences.
            - Every recommendation must reference a "scanItemId" exactly as given in the payload.
              Never invent an id and never write a real or guessed file-system path.
            - "actionSuggestion" must be one of: Move, Delete, Review, NoAction.
            - Only suggest Delete for items where "canDelete" is true, and Move where "canMove"
              is true. Prefer Move or Review when in doubt.
            - "estimatedSpaceGb" must not exceed the item's own "sizeGb".
            - "confidence" is a number between 0 and 1.
            - You are advisory only. Storava never executes anything you propose; the user
              selects and confirms every action themselves. Do not claim otherwise.
            {{reportRule}}

            Write all human-readable text in {{languageName}}.

            Respond with this exact shape:
            {
              "summary": "...",
              "mainCause": "...",
              "recommendations": [
                {
                  "scanItemId": "...",
                  "title": "...",
                  "actionSuggestion": "Move",
                  "reason": "...",
                  "risk": "Low",
                  "confidence": 0.9,
                  "estimatedSpaceGb": 12.5,
                  "warnings": ["..."]
                }
              ]{{reportShape}}
            }
            """;
    }

    public static string BuildUserPrompt(AiRequestPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, PayloadOptions);
    }

    /// <summary>Renders the payload exactly as the user will see it in the consent preview.</summary>
    public static string RenderPayloadForPreview(AiRequestPayload payload) => BuildUserPrompt(payload);
}
