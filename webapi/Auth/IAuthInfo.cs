// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using CopilotChat.WebApi.Extensions;

namespace CopilotChat.WebApi.Auth;

public interface IAuthInfo
{
    /// <summary>
    /// The authenticated user's unique ID.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// The authenticated user's groups.
    /// </summary>
    public IEnumerable<string>? UserGroups { get; }

    /// <summary>
    /// The authenticated user's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The authenticated user's graph extension.
    /// </summary>
    public GraphExtension? GraphExtension { get; }
}
