// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Orchestration;

namespace CopilotChat.WebApi.Plugins.Retriever.Document;

/// <summary>
/// Interface for document operation connections
/// </summary>
public interface IDocumentConnector
{
    /// <summary>
    /// Search for documents.
    /// </summary>
    Task<string> DocumentSearchHandlerAsync(
    string query,
    SKContext chatContext,
    CancellationToken cancellationToken
);
}