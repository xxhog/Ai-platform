using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ai.Models;

namespace ai.Services;

public sealed class OpenRouterService(HttpClient httpClient, IConfiguration configuration)
{
    private readonly string? _apiKey = FirstNonEmpty(configuration["OpenRouter:ApiKey"], configuration["OPENROUTER_API_KEY"]);
    private readonly string? _model = FirstNonEmpty(configuration["OpenRouter:Model"], configuration["OPENROUTER_MODEL"]);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);

    public async Task<string> StreamAsync(IReadOnlyCollection<ChatMessage> messages, Func<string, Task> onText, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Сначала задайте OpenRouter:ApiKey и OpenRouter:Model в локальных настройках приложения.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent.Create(new
        {
            model = _model,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            stream = true
        });

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = TryReadError(responseText);
            throw new HttpRequestException($"OpenRouter вернул {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }

        var answer = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            using var chunk = JsonDocument.Parse(data);
            if (!chunk.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var delta = choices[0].GetProperty("delta");
            if (!delta.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) continue;
            var text = content.GetString();
            if (string.IsNullOrEmpty(text)) continue;
            answer.Append(text);
            await onText(text);
        }
        return answer.ToString();
    }

    private static string TryReadError(string responseText)
    {
        try { return JsonDocument.Parse(responseText).RootElement.GetProperty("error").GetProperty("message").GetString() ?? "Ошибка запроса"; }
        catch (JsonException) { return "Ошибка запроса к модели"; }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
