using System.Text.Json;
using ai.Models;

namespace ai.Services;

public sealed class ConversationStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConversationStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "conversations.json");
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(cancellationToken))
                .OrderByDescending(item => item.UpdatedAt)
                .Select(Summary)
                .ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task<Conversation> CreateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadAsync(cancellationToken);
            var conversation = new Conversation();
            items.Add(conversation);
            await WriteAsync(items, cancellationToken);
            return conversation;
        }
        finally { _gate.Release(); }
    }

    public async Task<Conversation?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken)).FirstOrDefault(item => item.Id == id); }
        finally { _gate.Release(); }
    }

    public async Task<Conversation?> AddMessageAsync(string id, string content, string role = "user", CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadAsync(cancellationToken);
            var conversation = items.FirstOrDefault(item => item.Id == id);
            if (conversation is null) return null;
            var firstMessage = conversation.Messages.Count == 0;
            conversation.Messages.Add(new ChatMessage { Role = role, Content = content.Trim() });
            if (firstMessage && role == "user")
                conversation.Title = content.Trim().Length > 42 ? content.Trim()[..42] + "…" : content.Trim();
            conversation.UpdatedAt = DateTime.UtcNow;
            await WriteAsync(items, cancellationToken);
            return conversation;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RenameAsync(string id, string title, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadAsync(cancellationToken);
            var conversation = items.FirstOrDefault(item => item.Id == id);
            if (conversation is null) return false;
            conversation.Title = title.Trim();
            conversation.UpdatedAt = DateTime.UtcNow;
            await WriteAsync(items, cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadAsync(cancellationToken);
            var removed = items.RemoveAll(item => item.Id == id) > 0;
            if (removed) await WriteAsync(items, cancellationToken);
            return removed;
        }
        finally { _gate.Release(); }
    }

    private async Task<List<Conversation>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return [];
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Conversation>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteAsync(List<Conversation> conversations, CancellationToken cancellationToken)
    {
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, conversations, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }

    private static Conversation Summary(Conversation item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        Messages = []
    };
}
