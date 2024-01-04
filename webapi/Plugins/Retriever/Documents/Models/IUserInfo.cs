using System;
using System.Collections.Generic;

namespace CopilotChat.WebApi.Plugins.Retriever.Document;

public class IUserInfo
{
    /// <summary>
    /// user id
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// chat id
    /// </summary>
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// global document id
    /// </summary>
    public string GlobalDocumentId { get; set; } = Guid.Empty.ToString();

    /// <summary>
    /// user groups
    /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
    public string[] Groups { get; set; } = Array.Empty<string>();
#pragma warning restore CA1819 // Properties should not return arrays

    /// <summary>
    /// scope ids
    /// </summary>
    /// concatenates userid, chatid, and groups together
#pragma warning disable CA1819 // Properties should not return arrays
    public string[] ScopeIds
#pragma warning restore CA1819 // Properties should not return arrays
    {
        get
        {
            var scopeIds = new List<string>
            {
                this.UserId,
                this.ChatId,
                this.GlobalDocumentId
            };
            scopeIds.AddRange(this.Groups);
            return scopeIds.ToArray();
        }
    }
}