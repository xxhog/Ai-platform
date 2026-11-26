namespace ai.Models;

public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Новый диалог";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = [];
}

public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record CreateMessageRequest(string Content);
public sealed record RenameConversationRequest(string Title);
