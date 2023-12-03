// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace CopilotChat.WebApi.Models.Request;

/// <summary>
/// Form for importing a document from a POST Http request.
/// </summary>
public class DocumentStatusForm
{
    /// <summary>
    /// The file to import.
    /// </summary>
    public IEnumerable<string> FileReferences { get; set; } = Enumerable.Empty<string>();

    /// <summary>
    /// The scope IDs that can access the document.
    /// Can be user ID, group ID(s), chat ID(s), global, or any combination of the above.
    /// </summary>
    public IEnumerable<string> ScopeIds { get; set; } = Enumerable.Empty<string>();

    /// <summary>
    /// The ID of the user who is importing the document to a chat session.
    /// Will be use to validate if the user has access to the chat session.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the user who sent this message.
    /// Will be used to create the chat message representing the document upload.
    /// </summary>
    public string UserName { get; set; } = string.Empty;
}
