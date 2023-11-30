// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Extensions;
using CopilotChat.WebApi.Models.Request;
using CopilotChat.WebApi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;

namespace CopilotChat.WebApi.Plugins.Chat;

/// <summary>
/// Helper class to extract and create kernel memory from chat history.
/// </summary>
public static class SemanticChatMemoryCreator
{

    /// <summary>
    /// Extract and save kernel memory.
    /// </summary>
    /// <param name="chatId">The Chat ID.</param>
    /// <param name="kernel">The semantic kernel.</param>
    /// <param name="context">The Semantic Kernel context.</param>
    /// <param name="options">The prompts options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task CreateSemanticChatMemories(
        string chatId,
        List<string> semanticMemories,
        IKernelMemory memoryClient,
        PromptsOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        foreach (string memoryType in Enum.GetNames(typeof(SemanticMemoryType)))
        {
            try
            {
                if (!options.TryGetMemoryContainerName(memoryType, out var memoryName))
                {
                    logger.LogInformation("Unable to extract kernel memory for invalid memory type {0}. Continuing...", memoryType);
                    continue;
                }

                foreach (var item in semanticMemories)
                {
                    var formattedMemory = item;

                    await CreateMemoryAsync(memoryName, formattedMemory); // TODO: Check each but batch create
                }
            }
            catch (Exception ex) when (!ex.IsCriticalException())
            {
                // Skip kernel memory extraction for this item if it fails.
                // We cannot rely on the model to response with perfect Json each time.
                logger.LogInformation("Unable to extract kernel memory for {0}: {1}. Continuing...", memoryType, ex.Message);
                continue;
            }
        }

        /// <summary>
        /// Create a memory item in the memory collection.
        /// If there is already a memory item that has a high similarity score with the new item, it will be skipped.
        /// </summary>
        async Task CreateMemoryAsync(string memoryName, string memory)
        {
            try
            {
                // Search if there is already a memory item that has a high similarity score with the new item.
                var searchResult =
                    await memoryClient.SearchMemoryAsync(
                        options.MemoryIndexName,
                        memory,
                        options.SemanticMemoryRelevanceUpper,
                        resultCount: 1,
                        chatId,
                        memoryName,
                        cancellationToken);

                if (searchResult.Results.Count == 0)
                {
                    await memoryClient.StoreMemoryAsync(options.MemoryIndexName, chatId, memoryName, memory, cancellationToken: cancellationToken);
                }
            }
            catch (Exception exception) when (!exception.IsCriticalException())
            {
                // A store exception might be thrown if the collection does not exist, depending on the memory store connector.
                logger.LogError(exception, "Unexpected failure searching {0}", options.MemoryIndexName);
            }
        }
    }

    /// <summary>
    /// Create a completion settings object for chat response. Parameters are read from the PromptSettings class.
    /// </summary>
    private static OpenAIRequestSettings ToCompletionSettings(this PromptsOptions options)
    {
        var completionSettings = new OpenAIRequestSettings
        {
            MaxTokens = options.ResponseTokenLimit,
            Temperature = options.ResponseTemperature,
            TopP = options.ResponseTopP,
            FrequencyPenalty = options.ResponseFrequencyPenalty,
            PresencePenalty = options.ResponsePresencePenalty
        };

        return completionSettings;
    }
}
