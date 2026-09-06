using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed record AiProviderInfo(string Provider, string BaseUrl, string Model, bool RequiresApiKey, bool Local, string Status);

public sealed class AiProviderService(IHttpClientFactory httpClientFactory, CoreDbContext db)
{
    public async Task<AiProviderInfo> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var configured = await db.BusinessSettings.AsNoTracking().SingleAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(configured.AiProvider) && !string.Equals(configured.AiProvider, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var provider = configured.AiProvider.Trim().ToLowerInvariant();
            var baseUrl = configured.AiBaseUrl?.Trim().TrimEnd('/') ?? "";
            var model = configured.AiModel?.Trim() ?? "";
            var key = configured.AiApiKeyEncrypted?.Trim() ?? "";
            if (provider is "openai" && !string.IsNullOrWhiteSpace(key)) return new("openai", string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com" : baseUrl, string.IsNullOrWhiteSpace(model) ? "gpt-5" : model, true, false, "configured");
            if (provider is "ollama" && !string.IsNullOrWhiteSpace(model)) return new("ollama", string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl, model, false, true, "configured");
            if (provider is "lmstudio" && !string.IsNullOrWhiteSpace(model)) return new("lmstudio", string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:1234" : baseUrl, model, false, true, "configured");
            if (provider is "llamacpp" && !string.IsNullOrWhiteSpace(model)) return new("llamacpp", string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:8080" : baseUrl, model, false, true, "configured");
        }

        var ollama = await ProbeOllamaAsync(cancellationToken);
        if (ollama is not null) return ollama;
        var lm = await ProbeOpenAiCompatibleAsync("lmstudio", "http://127.0.0.1:1234", cancellationToken);
        if (lm is not null) return lm;
        var llama = await ProbeOpenAiCompatibleAsync("llamacpp", "http://127.0.0.1:8080", cancellationToken);
        if (llama is not null) return llama;

        var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
        if (!string.IsNullOrWhiteSpace(envKey)) return new("openai", Environment.GetEnvironmentVariable("OPENAI_BASE_URL")?.Trim().TrimEnd('/') ?? "https://api.openai.com", Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() ?? "gpt-5", true, false, "environment");
        return new("none", "", "", false, false, "no-provider");
    }

    public async Task<IReadOnlyList<AiProviderInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<AiProviderInfo>();
        var ollama = await ProbeOllamaAsync(cancellationToken); if (ollama is not null) result.Add(ollama);
        var lm = await ProbeOpenAiCompatibleAsync("lmstudio", "http://127.0.0.1:1234", cancellationToken); if (lm is not null) result.Add(lm);
        var llama = await ProbeOpenAiCompatibleAsync("llamacpp", "http://127.0.0.1:8080", cancellationToken); if (llama is not null) result.Add(llama);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))) result.Add(new("openai", Environment.GetEnvironmentVariable("OPENAI_BASE_URL")?.Trim().TrimEnd('/') ?? "https://api.openai.com", Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() ?? "gpt-5", true, false, "environment"));
        return result;
    }

    public async Task ConfigureAsync(string provider, string model, string? apiKey, string? baseUrl, CancellationToken cancellationToken = default)
    {
        var settings = await db.BusinessSettings.SingleAsync(cancellationToken);
        settings.AiProvider = string.IsNullOrWhiteSpace(provider) ? "auto" : provider.Trim().ToLowerInvariant();
        settings.AiModel = model?.Trim() ?? "";
        settings.AiBaseUrl = baseUrl?.Trim().TrimEnd('/') ?? "";
        if (apiKey is not null) settings.AiApiKeyEncrypted = apiKey.Trim();
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AiProviderInfo?> ProbeOllamaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("business-ai");
            using var response = await client.GetAsync("http://127.0.0.1:11434/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var model = json?["models"]?.AsArray().FirstOrDefault()?["name"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(model) ? null : new("ollama", "http://127.0.0.1:11434", model, false, true, "detected");
        }
        catch { return null; }
    }

    private async Task<AiProviderInfo?> ProbeOpenAiCompatibleAsync(string name, string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("business-ai");
            using var response = await client.GetAsync($"{baseUrl}/v1/models", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var model = json?["data"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(model) ? null : new(name, baseUrl, model, false, true, "detected");
        }
        catch { return null; }
    }

    public static HttpRequestMessage CreateRequest(AiProviderInfo provider, string path)
    {
        return new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl.TrimEnd('/') + path);
    }
}
