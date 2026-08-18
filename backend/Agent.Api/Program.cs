using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using AGUI.Abstractions;
using Agent.Api.Agents;
using Agent.Api.Data;
using Agent.Api.Llm;
using Agent.Api.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AGUIJsonSerializerContext.Default);
});

builder.Services.Configure<LlmOptions>(options =>
{
    builder.Configuration.GetSection(LlmOptions.SectionName).Bind(options);
    builder.Configuration.GetSection("OpenAI").Bind(options);

    options.ApiKey = FirstNonEmpty(
        options.ApiKey,
        builder.Configuration["Llm:ApiKey"],
        builder.Configuration["OpenAI:ApiKey"],
        builder.Configuration["LLM_API_KEY"],
        Environment.GetEnvironmentVariable("LLM_API_KEY"),
        Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ?? string.Empty;

    options.Model = FirstNonEmpty(
        options.Model,
        builder.Configuration["LLM_MODEL"],
        Environment.GetEnvironmentVariable("LLM_MODEL")) ?? "gpt-4o-mini";

    options.BaseUrl = FirstNonEmpty(
        options.BaseUrl,
        builder.Configuration["LLM_BASE_URL"],
        Environment.GetEnvironmentVariable("LLM_BASE_URL"));
});

builder.Services.AddSingleton<MockEmployeeService>();
builder.Services.AddSingleton<IAgentTool, GetEmployeeTool>();
builder.Services.AddSingleton<IAgentTool, GetLeaveBalanceTool>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<ILlmProvider, OpenAiLlmProvider>();
builder.Services.AddSingleton<AgentRunService>();

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

app.MapPost("/api/agent/run", (
    RunAgentInput input,
    AgentRunService agentRunService,
    CancellationToken cancellationToken) =>
{
    if (!AgentRunService.TryValidate(input, out var error))
    {
        return Results.BadRequest(new { error });
    }

    return TypedResults.ServerSentEvents(
        WrapAsSseItems(agentRunService.RunAsync(input, cancellationToken), cancellationToken));
});

app.Run();

static async IAsyncEnumerable<SseItem<BaseEvent>> WrapAsSseItems(
    IAsyncEnumerable<BaseEvent> events,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var evt in events.WithCancellation(cancellationToken))
    {
        yield return new SseItem<BaseEvent>(evt);
    }
}

static string? FirstNonEmpty(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
