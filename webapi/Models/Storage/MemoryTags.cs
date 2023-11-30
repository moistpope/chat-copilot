// Copyright (c) Microsoft. All rights reserved.

namespace CopilotChat.WebApi.Models.Storage;

/// <summary>
/// Tag names for kernel memory.
/// </summary>
internal static class MemoryTags
{
    /// <summary>
    /// Associates memory with a specific group
    /// </summary>
    public const string TagGroupId = "groupid";

    /// <summary>
    /// Associates memory with a specific user
    /// </summary>
    public const string TagUserId = "userid";

    /// <summary>
    /// Associates memory with a specific chat
    /// </summary>
    public const string TagChatId = "chatid";

    /// <summary>
    /// Associates memory with specific type.
    /// </summary>
    public const string TagMemory = "memory";
}
