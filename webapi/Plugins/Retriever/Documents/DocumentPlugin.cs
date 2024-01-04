// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;

namespace CopilotChat.WebApi.Plugins.Retriever.Document;

/// <summary>
/// Document plugin.
/// </summary>
public class DocumentPlugin
{
    private readonly IDocumentConnector _connector;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentPlugin"/> class.
    /// </summary>
    /// <param name="connector">Document connector.</param>
    /// <param name="loggerFactory">The <see cref="ILoggerFactory"/> to use for logging. If null, no logging will be performed.</param>
    public DocumentPlugin(IDocumentConnector connector, ILoggerFactory? loggerFactory = null)
    {
        System.Diagnostics.Debug.Assert(connector != null, "connector must not be null");

        this._connector = connector;
        this._logger = loggerFactory?.CreateLogger(typeof(DocumentPlugin)) ?? NullLogger.Instance;
    }

    /// <summary>
    /// Search for documents.
    /// </summary>
    [SKFunction, Description("Search knowledge base for relevant company information, policies, and procedures.")]
    public async Task<string> SearchDocuments(
        [Description("Search query along with any relevant context")] string query,
        SKContext chatContext
    )
    {
        return await this._connector.DocumentSearchHandlerAsync(query, chatContext, CancellationToken.None);
    }
}