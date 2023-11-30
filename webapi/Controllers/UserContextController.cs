// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using CopilotChat.WebApi.Auth;
using CopilotChat.WebApi.Extensions;
using CopilotChat.WebApi.Models.Request;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.Graph;
using Microsoft.SemanticKernel.Plugins.MsGraph.Connectors.Client;
using System.Threading;
using Microsoft.SemanticKernel;
using CopilotChat.WebApi.Hubs;
using CopilotChat.WebApi.Plugins.Chat;
using CopilotChat.WebApi.Utilities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel.Functions.OpenAPI.Authentication;

namespace CopilotChat.WebApi.Controllers;

/// <summary>
/// Controller for retrieving kernel memory data of chat sessions.
/// </summary>
[ApiController]
public class UserContextController : ControllerBase
{
    private readonly ILogger<ChatMemoryController> _logger;
    private readonly PromptsOptions _promptOptions;
    private readonly ChatSessionRepository _chatSessionRepository;
    private readonly CancellationToken _cancellationToken;
    private GraphServiceClient? _graphServiceClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMemoryController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="promptsOptions">The prompts options.</param>
    /// <param name="chatSessionRepository">The chat session repository.</param>
    public UserContextController(
        ILogger<ChatMemoryController> logger,
        IOptions<PromptsOptions> promptsOptions,
        ChatSessionRepository chatSessionRepository)
    {
        this._logger = logger;
        this._promptOptions = promptsOptions.Value;
        this._chatSessionRepository = chatSessionRepository;
        this._cancellationToken = default;
        // this._graphServiceClient = graphServiceClient;
    }
    /// <summary>
    /// Gets the kernel memory for the chat session.
    /// </summary>
    /// <param name="semanticTextMemory">The semantic text memory instance.</param>
    /// <param name="chatId">The chat id.</param>
    /// <param name="type">Type of memory. Must map to a member of <see cref="SemanticMemoryType"/>.</param>
    [Route("chats/{chatId:guid}/refreshUserData")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [Authorize(Policy = AuthPolicyName.RequireChatParticipant)]
    public async Task<IActionResult> RefreshUserDataAsync(
        [FromServices] IKernel kernel,
        [FromServices] IHubContext<MessageRelayHub> messageRelayHubContext,
        [FromServices] CopilotChatPlanner planner,
        [FromServices] AskConverter askConverter,
        [FromServices] ChatSessionRepository chatSessionRepository,
        [FromServices] ChatParticipantRepository chatParticipantRepository,
        [FromServices] IAuthInfo authInfo,
        [FromServices] IKernelMemory memoryClient,
        [FromRoute] string chatId,
        [FromBody] Ask ask)
    {
        // Sanitize the log input by removing new line characters.
        // https://github.com/microsoft/chat-copilot/security/code-scanning/1
        var sanitizedChatId = GetSanitizedParameter(chatId);

        var authHeaders = this.GetPluginAuthHeaders(this.HttpContext.Request.Headers);
        if (authHeaders.TryGetValue("GRAPH", out string? GraphAuthHeader))
        {
            try
            {
                this._logger.LogInformation("Enabling Microsoft Graph plugin(s).");
                BearerAuthenticationProvider authenticationProvider = new(() => Task.FromResult(GraphAuthHeader));
                this._graphServiceClient = this.CreateGraphServiceClient(authenticationProvider.AuthenticateRequestAsync);

                this._logger.LogInformation("Loading user data to memory in chat: {0}...", sanitizedChatId);

                return await this.HandleRequest(
                sanitizedChatId,
                kernel,
                memoryClient);
            }
            catch (Exception ex) when (!ex.IsCriticalException())
            {
                this._logger.LogError(ex, "UserContextController: Unable to create GraphServiceClient: {0}", ex.Message);
                return this.Ok();
            }
        }
        else
        {
            this._logger.LogInformation("No Graph auth header found. Skipping Graph plugin(s).");
            return this.Ok();
        }
    }

    private async Task<IActionResult> HandleRequest(
      string chatId,
      IKernel kernel,
      IKernelMemory memoryClient)
    {
        var options = this._promptOptions;
        var cancellationToken = this._cancellationToken;
        var logger = this._logger;

        foreach (string memoryType in Enum.GetNames(typeof(SemanticMemoryType)))
        {
            try
            {
                if (!options.TryGetMemoryContainerName(memoryType, out var memoryName))
                {
                    logger.LogInformation("Unable to extract kernel memory for invalid memory type {0}. Continuing...", memoryType);
                    continue;
                }

                var authHeaders = this.GetPluginAuthHeaders(this.HttpContext.Request.Headers);
                if (authHeaders.TryGetValue("GRAPH", out string? GraphAuthHeader))
                {
                    this._logger.LogInformation("Enabling Microsoft Graph plugin(s).");
                    BearerAuthenticationProvider authenticationProvider = new(() => Task.FromResult(GraphAuthHeader));
                    GraphServiceClient graphServiceClient = this.CreateGraphServiceClient(authenticationProvider.AuthenticateRequestAsync);
                }

                var graphClient = this._graphServiceClient;
                var userData = await graphClient?.Me.Request().GetAsync(cancellationToken);

                //mock semanticMemory
                SemanticChatMemory semanticMemory = new();

                foreach (var item in semanticMemory.Items)
                {
                    await CreateMemoryAsync(memoryName, item.ToFormattedString());
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

        return this.Ok();

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
    /// Parse plugin auth values from request headers.
    /// </summary>
    private Dictionary<string, string> GetPluginAuthHeaders(IHeaderDictionary headers)
    {
        // Create a regex to match the headers
        var regex = new Regex("x-sk-copilot-(.*)-auth", RegexOptions.IgnoreCase);

        // Create a dictionary to store the matched headers and values
        var authHeaders = new Dictionary<string, string>();

        // Loop through the request headers and add the matched ones to the dictionary
        foreach (var header in headers)
        {
            var match = regex.Match(header.Key);
            if (match.Success)
            {
                // Use the first capture group as the key and the header value as the value
                authHeaders.Add(match.Groups[1].Value.ToUpperInvariant(), header.Value!);
            }
        }

        return authHeaders;
    }

    /// <summary>
    /// Create a Microsoft Graph service client.
    /// </summary>
    /// <param name="authenticateRequestAsyncDelegate">The delegate to authenticate the request.</param>
    private GraphServiceClient CreateGraphServiceClient(AuthenticateRequestAsyncDelegate authenticateRequestAsyncDelegate)
    {
        using MsGraphClientLoggingHandler graphLoggingHandler = new(this._logger);

        IList<DelegatingHandler> graphMiddlewareHandlers =
            GraphClientFactory.CreateDefaultHandlers(new DelegateAuthenticationProvider(authenticateRequestAsyncDelegate));
        graphMiddlewareHandlers.Add(graphLoggingHandler);

        using HttpClient graphHttpClient = GraphClientFactory.Create(graphMiddlewareHandlers);

        GraphServiceClient graphServiceClient = new(graphHttpClient);
        return graphServiceClient;
    }

    #region Private

    private static string GetSanitizedParameter(string parameterValue)
    {
        return parameterValue.Replace(Environment.NewLine, string.Empty, StringComparison.Ordinal);
    }

    # endregion
}
