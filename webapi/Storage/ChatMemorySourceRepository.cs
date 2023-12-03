// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CopilotChat.WebApi.Models.Storage;

namespace CopilotChat.WebApi.Storage;

/// <summary>
/// A repository for chat messages.
/// </summary>
public class ChatMemorySourceRepository : Repository<MemorySource>
{
    /// <summary>
    /// Initializes a new instance of the ChatMemorySourceRepository class.
    /// </summary>
    /// <param name="storageContext">The storage context.</param>
    public ChatMemorySourceRepository(IStorageContext<MemorySource> storageContext)
        : base(storageContext)
    {
    }

    /// <summary>
    /// Finds chat memory sources by user, chat, or group id
    /// Currently a mask for FindByChatIdAsync as userIds, chatIds, and global are expanded to explicit permissions
    /// </summary>
    /// <param name="scopeIds">The chat session id.</param>
    /// <param name="includeGlobal">Flag specifying if global documents should be included in the response.</param>
    /// <returns>A list of memory sources.</returns>
    public Task<IEnumerable<MemorySource>> FindByScopeIdAsync(string[] scopeIds, bool includeGlobal = true)
    {
        return base.StorageContext.QueryEntitiesAsync(e => scopeIds.Any(gid => e.ScopeIds.Contains(gid)) || (includeGlobal && (e.ScopeIds.Contains(Guid.Empty.ToString()))));
    }

    public Task<IEnumerable<MemorySource>> FindByScopeIdAsync(string scopeId, bool includeGlobal = true)
    {
        return base.StorageContext.QueryEntitiesAsync(e => e.ScopeIds.Contains(scopeId) || (includeGlobal && (e.ScopeIds.Contains(Guid.Empty.ToString()))));
    }

    /// <summary>
    /// Finds chat memory sources by chat id
    /// </summary>
    /// <param name="chatId">The chat session id.</param>
    /// <returns>A list of memory sources.</returns>
    public Task<IEnumerable<MemorySource>> FindByChatIdAsync(string chatId)
    {
        return base.StorageContext.QueryEntitiesAsync(e => e.ScopeIds.Contains(chatId));
    }

    /// <summary>
    /// Finds chat memory sources by name
    /// </summary>
    /// <param name="name">Name</param>
    /// <returns>A list of memory sources with the given name.</returns>
    public Task<IEnumerable<MemorySource>> FindByNameAsync(string name)
    {
        return base.StorageContext.QueryEntitiesAsync(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Retrieves all memory sources.
    /// </summary>
    /// <returns>A list of memory sources.</returns>
    public Task<IEnumerable<MemorySource>> GetAllAsync()
    {
        return base.StorageContext.QueryEntitiesAsync(e => true);
    }
}
