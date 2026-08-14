using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using AGUI.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AGUIJsonSerializerContext.Default);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/agent/run", (RunAgentInput input, CancellationToken cancellationToken) =>
{
    var threadId = string.IsNullOrWhiteSpace(input.ThreadId)
        ? Guid.NewGuid().ToString("N")
        : input.ThreadId;

    var runId = string.IsNullOrWhiteSpace(input.RunId)
        ? Guid.NewGuid().ToString("N")
        : input.RunId;

    return TypedResults.ServerSentEvents(StreamAgUiEvents(threadId, runId, cancellationToken));
});

app.Run();

static async IAsyncEnumerable<SseItem<BaseEvent>> StreamAgUiEvents(
    string threadId,
    string runId,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    // Phase 2: hardcoded AG-UI lifecycle — no LLM.
    yield return new SseItem<BaseEvent>(new RunStartedEvent
    {
        ThreadId = threadId,
        RunId = runId
    });

    var messageId = Guid.NewGuid().ToString("N");

    yield return new SseItem<BaseEvent>(new TextMessageStartEvent
    {
        MessageId = messageId,
        Role = "assistant"
    });

    // Visible streaming: "Hello from AG-UI"
    string[] chunks = ["Hello", " from", " AG-UI"];

    foreach (var chunk in chunks)
    {
        await Task.Delay(450, cancellationToken);
        yield return new SseItem<BaseEvent>(new TextMessageContentEvent
        {
            MessageId = messageId,
            Delta = chunk
        });
    }

    yield return new SseItem<BaseEvent>(new TextMessageEndEvent
    {
        MessageId = messageId
    });

    yield return new SseItem<BaseEvent>(new RunFinishedEvent
    {
        ThreadId = threadId,
        RunId = runId
    });
}
