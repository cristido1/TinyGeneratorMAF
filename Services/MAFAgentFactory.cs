using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI;
using TinyGenerator.Models;

namespace TinyGenerator.Services
{
    /// <summary>
    /// Factory for creating and managing Microsoft Agent Framework (MAF) agents.
    /// Replaces KernelFactory for MAF-based agent orchestration.
    /// </summary>
    public class MAFAgentFactory
    {
        private readonly ConcurrentDictionary<int, IChatClient> _agentClients = new();
        private readonly IConfiguration _config;
        private readonly ILogger<MAFAgentFactory>? _logger;
        private readonly DatabaseService _database;
        private readonly PersistentMemoryService _memoryService;
        private readonly IServiceProvider _serviceProvider;

        public MAFAgentFactory(
            IConfiguration config,
            DatabaseService database,
            PersistentMemoryService memoryService,
            IServiceProvider serviceProvider,
            ILogger<MAFAgentFactory>? logger = null)
        {
            _config = config;
            _database = database;
            _memoryService = memoryService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Creates an IChatClient configured for the specified model
        /// </summary>
        public IChatClient CreateChatClient(string? modelId = null)
        {
            var modelInfo = ResolveModelInfo(modelId);
            
            if (modelInfo == null)
            {
                _logger?.LogWarning("No model info found for {ModelId}, using default Ollama configuration", modelId);
                return CreateOllamaChatClient("phi3:mini");
            }

            // Determine provider from model info
            var provider = modelInfo.Provider?.ToLowerInvariant() ?? "ollama";
            var endpoint = modelInfo.Endpoint;
            var modelName = modelInfo.Name ?? modelId ?? "phi3:mini";

            return provider switch
            {
                "ollama" => CreateOllamaChatClient(modelName, endpoint),
                "openai" => CreateOpenAIChatClient(modelName),
                "azure" or "azureopenai" => CreateAzureOpenAIChatClient(modelName, endpoint),
                _ => CreateOllamaChatClient(modelName, endpoint)
            };
        }

        /// <summary>
        /// Creates an Ollama chat client
        /// </summary>
        private IChatClient CreateOllamaChatClient(string modelName, string? endpoint = null)
        {
            var ollamaEndpoint = endpoint ?? _config["Ollama:Endpoint"] ?? "http://localhost:11434/v1/";
            
            _logger?.LogInformation("Creating Ollama chat client: {Model} @ {Endpoint}", modelName, ollamaEndpoint);

            // Use OpenAI-compatible endpoint for Ollama
            var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential("ollama"), new OpenAIClientOptions
            {
                Endpoint = new Uri(ollamaEndpoint)
            });

            var chatClient = (IChatClient)client.GetChatClient(modelName);
            return chatClient.AsBuilder().UseFunctionInvocation().Build();
        }

        /// <summary>
        /// Creates an OpenAI chat client
        /// </summary>
        private IChatClient CreateOpenAIChatClient(string modelName)
        {
            var apiKey = _config["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("OpenAI API key not configured");
            }

            _logger?.LogInformation("Creating OpenAI chat client: {Model}", modelName);

            var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey));
            var chatClient = (IChatClient)client.GetChatClient(modelName);
            return chatClient.AsBuilder().UseFunctionInvocation().Build();
        }

        /// <summary>
        /// Creates an Azure OpenAI chat client
        /// </summary>
        private IChatClient CreateAzureOpenAIChatClient(string deploymentName, string? endpoint = null)
        {
            var azureEndpoint = endpoint ?? _config["AzureOpenAI:Endpoint"];
            
            if (string.IsNullOrEmpty(azureEndpoint))
            {
                throw new InvalidOperationException("Azure OpenAI endpoint not configured");
            }

            _logger?.LogInformation("Creating Azure OpenAI chat client: {Deployment} @ {Endpoint}", deploymentName, azureEndpoint);

            var client = new AzureOpenAIClient(
                new Uri(azureEndpoint),
                new DefaultAzureCredential()
            );

            var chatClient = (IChatClient)client.GetChatClient(deploymentName);
            return chatClient.AsBuilder().UseFunctionInvocation().Build();
        }

        /// <summary>
        /// Gets or creates a cached chat client for an agent
        /// </summary>
        public IChatClient GetOrCreateClientForAgent(int agentId)
        {
            return _agentClients.GetOrAdd(agentId, id =>
            {
                var agent = _database.GetAgentById(id);
                if (agent == null)
                {
                    throw new InvalidOperationException($"Agent {id} not found");
                }

                var modelInfo = agent.ModelId.HasValue 
                    ? _database.GetModelInfoById(agent.ModelId.Value) 
                    : null;

                return CreateChatClient(modelInfo?.Name);
            });
        }

        /// <summary>
        /// Resolves model info from model ID string or database
        /// </summary>
        private ModelInfo? ResolveModelInfo(string? modelId)
        {
            if (string.IsNullOrEmpty(modelId))
            {
                return null;
            }

            // Try to parse as numeric ID
            if (int.TryParse(modelId, out var numericId))
            {
                return _database.GetModelInfoById(numericId);
            }

            // Try to find by name
            var models = _database.ListModels();
            return models.FirstOrDefault(m => 
                m.Name?.Equals(modelId, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Creates AI functions from a text plugin for MAF
        /// </summary>
        public static IEnumerable<AIFunction> CreateTextPluginFunctions()
        {
            return new[]
            {
                AIFunctionFactory.Create(
                    (string input) => input.ToUpperInvariant(),
                    name: "toupper",
                    description: "Converts a string to uppercase."
                ),
                AIFunctionFactory.Create(
                    (string input) => input.ToLowerInvariant(),
                    name: "tolower",
                    description: "Converts a string to lowercase."
                ),
                AIFunctionFactory.Create(
                    (string input) => input.Trim(),
                    name: "trim",
                    description: "Trims whitespace from the start and end of a string."
                ),
                AIFunctionFactory.Create(
                    (string input) => input?.Length ?? 0,
                    name: "length",
                    description: "Gets the length of a string."
                )
            };
        }

        /// <summary>
        /// Clears cached clients (useful for testing or config changes)
        /// </summary>
        public void ClearCache()
        {
            _agentClients.Clear();
            _logger?.LogInformation("MAF agent client cache cleared");
        }
    }
}
