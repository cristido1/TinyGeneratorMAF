# AI Coding Assistant Instructions for TinyGeneratorMAF

## Project Overview
TinyGeneratorMAF is an ASP.NET Core web application migrating from Semantic Kernel to **Microsoft Agent Framework (MAF)** for AI agent orchestration. The app generates stories using multiple writer agents (local Ollama models) and evaluator agents to produce coherent, structured narratives.

## Architecture
- **Core Service**: `StoryGeneratorService` coordinates story generation via multiple writer agents (A, B, C)
- **MAF Integration** (NEW): `MAFAgentFactory` creates IChatClient instances for Ollama/OpenAI/Azure with function invocation support
- **Legacy SK Support**: `KernelFactory` still active for gradual migration
- **Persistence**: `DatabaseService` handles SQLite storage for stories, logs, models, agents, and test results
- **UI**: Razor Pages (e.g., `Genera.cshtml`) with Bootstrap/DataTables for admin interfaces. SignalR (`ProgressHub`) for live generation updates
- **Agents**: Defined in DB with JSON configs for skills, prompts, and execution plans. Active agents get dedicated clients at startup
- **Skills/Functions**: 
  - SK: Custom classes in `Skills/` with `[KernelFunction]` methods
  - MAF: `AIFunctionFactory.Create()` for direct function registration

## Key Workflows
- **Build**: `dotnet build` or VS Code task "build"
- **Run/Debug**: `dotnet run` or `dotnet watch run` (task "watch") for hot reload
- **Test Models**: Use `Pages/Models.cshtml` to run function-calling tests on Ollama models
- **Generate Stories**: Via `Pages/Genera.cshtml` - inputs theme, selects writer (All/A/B/C), monitors progress via SignalR
- **Admin**: Manage agents, models, logs in admin pages using DataTables for CRUD

## Migration Status: SK → MAF
**Phase 1: Foundation (CURRENT)**
- ✅ MAF packages added (Microsoft.Agents.AI, Microsoft.Extensions.AI 10.0.0)
- ✅ `MAFAgentFactory` created with Ollama/OpenAI/Azure support
- ✅ Function calling infrastructure via `AIFunctionFactory.Create()`
- ✅ Registered in DI container (Program.cs)
- 🚧 Test page for validation (in progress)
- ⏳ Writer agent migration (next phase)

**Phase 2: Agent Migration (PLANNED)**
- Migrate StoryGeneratorService to use MAFAgentFactory
- Convert writer agents A, B, C to IChatClient
- Convert evaluator agents to IChatClient
- Parallel support for SK and MAF during transition

**Phase 3: Cleanup (FUTURE)**
- Remove SK dependencies after full validation
- Update all documentation
- Performance benchmarking

## Conventions

### MAF Integration
- **Chat Client Creation**: Use `MAFAgentFactory.CreateChatClient(modelId)` for IChatClient instances
- **Function Registration**: Use `AIFunctionFactory.Create(method, name, description)` instead of `[KernelFunction]`
- **Function Invocation**: Clients auto-configured with `.AsBuilder().UseFunctionInvocation().Build()`
- **Ollama Endpoints**: Default `http://localhost:11434/v1/` (OpenAI-compatible API)
- **Model Resolution**: Queries `models` table in DB to resolve model ID → endpoint/provider

### Legacy SK Integration (During Migration)
- **Semantic Kernel**: All AI interactions use SK's function-calling mechanism. No parsing model outputs for actions
- **Agent Prompts**: Keep production prompts separate from test prompts. Avoid "invented" functions
- **Skill Registration**: Plugins added via `builder.Plugins.AddFromObject(skill, "alias")` in `KernelFactory`
- **Database**: Use Dapper for queries. Tables: models, agents, stories, calls, test_definitions, etc.

### Common Patterns
- **UI Patterns**: Razor Pages with Tag Helpers (`asp-for`), Bootstrap 5, DataTables for tables
- **Logging**: Custom `ICustomLogger` to DB. Broadcast logs via `NotificationService` and SignalR
- **Startup**: Initializes DB schema, seeds models/voices, creates clients/kernels for active agents
- **Error Handling**: Fail-fast on integration issues to highlight problems

## Examples

### MAF Function Calling (NEW)
```csharp
// Create functions using AIFunctionFactory
var functions = new[]
{
    AIFunctionFactory.Create(
        (string input) => input.ToUpperInvariant(),
        name: "toupper",
        description: "Converts a string to uppercase."
    )
};

// Create client with function invocation
var client = mafFactory.CreateChatClient("phi3:mini")
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

// Use in chat with tools
var messages = new List<ChatMessage>
{
    new(ChatRole.User, "Convert 'hello' to uppercase")
};

var options = new ChatOptions { Tools = functions.Cast<AITool>().ToList() };
var response = await client.CompleteAsync(messages, options);
```

### Legacy SK Function Calling
```csharp
// Legacy SK skill
[KernelFunction("calculate")]
public double Add(double a, double b) => a + b;

// Register in KernelFactory
kernel.Plugins.Add(KernelPluginFactory.CreateFromType<MathSkill>());
```

### Agent Configuration (DB)
```json
{
  "Skills": ["text", "memory"],
  "ModelId": 5,
  "Role": "writer",
  "Instructions": "You are a creative writer...",
  "ExecutionPlan": "..."
}
```

### Story Generation Flow
1. Writers use models like `phi3:mini-128k` (Ollama)
2. Evaluators score on JSON format (e.g. `{"score": 8}`)
3. Best story saved if score >= 7
4. TTS generation via external API (optional)

## Notes
- **Dual Support**: Both SK and MAF active during migration - services choose based on feature flags
- **Models**: Configured in DB (`models` table) with provider/endpoint/name
- **TTS**: External API, voices seeded from service
- **Memory**: Persistent SQLite via `PersistentMemoryService`
- **Testing Philosophy**: Prefer failing tests to expose integration issues rather than silent fallbacks
- **MAF Benefits**: Simpler API, better multi-agent orchestration, direct function registration (no plugin wrappers)</content>
<parameter name="filePath">/Users/cristianodonaggio/Documents/ai/TinyGenerator/.github/copilot-instructions.md