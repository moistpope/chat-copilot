using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Models.Storage;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Plugins.Chat;
using HandlebarsDotNet.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI.AzureSdk;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.TemplateEngine;
using static Microsoft.SemanticKernel.TemplateEngine.PromptTemplateConfig;

namespace CopilotChat.WebApi.Plugins.Retriever.Document.Connectors;

public class KernelMemoryConnector : IDocumentConnector
{
    private readonly IKernel _kernel;
    private readonly IKernelMemory _memoryClient;

    private static readonly List<string> pipelineSteps = new() { "extract", "partition", "gen_embeddings", "save_embeddings" };

    private const string memoryName = "DocumentMemory";
    private const string indexName = "chatmemory";
    private const float relevanceThreshold = 0.8f;

    private readonly IUserInfo _userInfo = new();

    private readonly PromptsOptions _promptOptions;
    private static readonly string[] stopSequences = new string[] { "] bot:" };

    public KernelMemoryConnector(
        IKernel kernel,
        IKernelMemory memoryClient,
        IUserInfo userInfo,
        PromptsOptions promptOptions
        )
    {
        this._kernel = kernel;
        this._memoryClient = memoryClient;
        this._userInfo = userInfo;
        this._promptOptions = promptOptions;
    }

    public async Task<string> DocumentSearchHandlerAsync(
        string query,
        SKContext chatContext,
        CancellationToken cancellationToken = default)
    {
        var userId = this._userInfo.UserId;
        var chatId = this._userInfo.ChatId;
        var scopeIds = this._userInfo.ScopeIds;
        var filters = new List<MemoryFilter>();

        // query = await this.HyDEQuery(chatId, chatContext, cancellationToken);

        var retrievedDocuments = (await SearchDocuments()).Results;

        retrievedDocuments = retrievedDocuments
                .GroupBy(m => m.Link)
                .Select(g => g.First())
                .ToList();

        string output = string.Empty;

        foreach (var document in retrievedDocuments)
        {
            var memoryText = $"Document name: {document.SourceName}\nDocument link: {document.Link}.\n[CONTENT START]\n{string.Join("\n", document.Partitions.Select(p => p.Text))}\n[CONTENT END]\n";
            output += memoryText;
        }

        return output;

        async Task<SearchResult> SearchDocuments()
        {
            for (int i = 0; i < scopeIds.Length; i++)
            {
                filters.Add(
                    MemoryFilters.ByTag(MemoryTags.TagScopeId, scopeIds[i])
                                 .ByTag(MemoryTags.TagMemory, memoryName)
                );
            }

            var searchResult =
                await this._memoryClient.SearchAsync(
                    query,
                    indexName,
                    null,
                    filters,
                    relevanceThreshold,
                    5,
                    cancellationToken);

            return searchResult;
        }
    
        
    }

    /// <summary>
    /// Modify user message to implement HyDE for more accurate retrieval.
    /// </summary>
    /// <param name="chatContext">The SKContext.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The modified user message as a query.</returns>
    [SKFunction, Description("Modify user message (with context) to implement HyDE for more accurate retrieval")]
    public async Task<string> HyDEQuery(
        [Description("Chat ID")] string chatId,
        [Description("Previous chat context")] SKContext chatContext,
        CancellationToken cancellationToken = default)
    {
        //get chat messages
        var chatHistoryFunction = this._kernel.Functions.GetFunction("ChatPlugin.ExtractChatHistory");
        var chatHistory = (await chatHistoryFunction.InvokeAsync(chatContext, cancellationToken: cancellationToken)).ToString();

        // ChatPlugin.ExtractChatHistory

        var completionFunction = this._kernel.CreateSemanticFunction(
            this._promptOptions.SystemRAG,
            pluginName: nameof(DocumentPlugin),
            promptTemplateConfig: new PromptTemplateConfig()
            {
                Input = new InputConfig()
                {
                    Parameters = new List<InputParameter>() {
                        new() {
                            Name = "context",
                            DefaultValue = chatHistory
                        }
                    }
                }
            });

        var result = await completionFunction.InvokeAsync(
        chatContext,
        new OpenAIRequestSettings
        {
            MaxTokens = this._promptOptions.ResponseTokenLimit,
            Temperature = this._promptOptions.IntentTemperature,
            TopP = this._promptOptions.IntentTopP,
            FrequencyPenalty = this._promptOptions.IntentFrequencyPenalty,
            PresencePenalty = this._promptOptions.IntentPresencePenalty,
            StopSequences = stopSequences
        },
        cancellationToken
        );

        // return result.ToString();
        return string.Empty;
    }




















    public static async Task StoreDocumentAsync(
        IKernelMemory memoryClient,
        string indexName,
        string documentId,
        IEnumerable<string> scopeIds,
        string createdBy,
        string memoryName,
        string fileName,
        Stream fileContent,
        CancellationToken cancellationToken = default)
    {
        var uploadRequest =
            new DocumentUploadRequest
            {
                DocumentId = documentId,
                Files = new List<DocumentUploadRequest.UploadedFile> { new(fileName, fileContent) },
                Index = indexName,
                Steps = pipelineSteps,
            };

        uploadRequest.Tags.Add(MemoryTags.TagCreatedBy, createdBy);

        uploadRequest.Tags.Add(MemoryTags.TagScopeId, scopeIds.Select(id => (string?)id).ToList());

        uploadRequest.Tags.Add(MemoryTags.TagMemory, memoryName);

        await memoryClient.ImportDocumentAsync(uploadRequest, cancellationToken);
    }

    public static async Task StoreMemoryAsync(
        IKernelMemory memoryClient,
        string indexName,
        string chatId,
        string memoryName,
        string memoryId,
        string memory,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream);
        await writer.WriteAsync(memory);
        await writer.FlushAsync();
        stream.Position = 0;

        var uploadRequest = new DocumentUploadRequest
        {
            DocumentId = memoryId,
            Index = indexName,
            Files =
                new()
                {
                    // Document file name not relevant, but required.
                    new DocumentUploadRequest.UploadedFile("memory.txt", stream)
                },
            Steps = pipelineSteps,
        };

        uploadRequest.Tags.Add(MemoryTags.TagScopeId, chatId);

        uploadRequest.Tags.Add(MemoryTags.TagMemory, memoryName);

        await memoryClient.ImportDocumentAsync(uploadRequest, cancellationToken);
    }
}
