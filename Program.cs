using ai.Models;
using ai.Services;
using System.Text.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.AddHttpClient<OpenRouterService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "ai-platform-api" }));
var conversations = app.MapGroup("/api/conversations");
conversations.MapGet("/", async (ConversationStore store, CancellationToken token) =>
    Results.Ok(await store.ListAsync(token)));
conversations.MapPost("/", async (ConversationStore store, CancellationToken token) =>
    Results.Ok(await store.CreateAsync(token)));
conversations.MapGet("/{id}", async (string id, ConversationStore store, CancellationToken token) =>
    await store.GetAsync(id, token) is { } item ? Results.Ok(item) : Results.NotFound());
conversations.MapPost("/{id}/messages", async (string id, CreateMessageRequest request, ConversationStore store, CancellationToken token) =>
{
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 20000)
        return Results.BadRequest(new { error = "Сообщение должно содержать от 1 до 20 000 символов." });
    return await store.AddMessageAsync(id, request.Content, cancellationToken: token) is { } item
        ? Results.Ok(item)
        : Results.NotFound();
});
conversations.MapPost("/{id}/chat", async (string id, CreateMessageRequest request, ConversationStore store, OpenRouterService openRouter, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 20000)
        return Results.BadRequest(new { error = "Сообщение должно содержать от 1 до 20 000 символов." });
    if (!openRouter.IsConfigured)
        return Results.Problem("OpenRouter пока не настроен. Добавьте ключ API и ID модели в локальные настройки приложения.", statusCode: 503);

    var conversation = await store.AddMessageAsync(id, request.Content, cancellationToken: context.RequestAborted);
    if (conversation is null) return Results.NotFound();
    context.Response.ContentType = "text/event-stream; charset=utf-8";
    context.Response.Headers.CacheControl = "no-cache";
    var answerBuffer = new StringBuilder();
    try
    {
        var answer = await openRouter.StreamAsync(conversation.Messages, async text =>
        {
            answerBuffer.Append(text);
            var data = JsonSerializer.Serialize(new { text });
            await context.Response.WriteAsync($"data: {data}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }, context.RequestAborted);
        if (!string.IsNullOrWhiteSpace(answer))
            await store.AddMessageAsync(id, answer, "assistant", context.RequestAborted);
        await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        if (answerBuffer.Length > 0)
            await store.AddMessageAsync(id, answerBuffer.ToString(), "assistant", CancellationToken.None);
    }
    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
    {
        var message = JsonSerializer.Serialize(new { error = ex.Message });
        await context.Response.WriteAsync($"event: error\ndata: {message}\n\n");
        await context.Response.Body.FlushAsync();
    }
    return Results.Empty;
});
conversations.MapPut("/{id}", async (string id, RenameConversationRequest request, ConversationStore store, CancellationToken token) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 80)
        return Results.BadRequest(new { error = "Название должно содержать от 1 до 80 символов." });
    return await store.RenameAsync(id, request.Title, token) ? Results.NoContent() : Results.NotFound();
});
conversations.MapDelete("/{id}", async (string id, ConversationStore store, CancellationToken token) =>
    await store.DeleteAsync(id, token) ? Results.NoContent() : Results.NotFound());

app.Run();
