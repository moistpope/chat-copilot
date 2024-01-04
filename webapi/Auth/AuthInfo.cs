// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Web;
using CopilotChat.WebApi.Extensions;

namespace CopilotChat.WebApi.Auth;

/// <summary>
/// Class which provides validated security information for use in controllers.
/// </summary>
public class AuthInfo : IAuthInfo
{
    private record struct AuthData(
        string UserId,
        string UserName
        );

    private readonly Lazy<AuthData> _data;
    private GraphExtension? _graphExtension;
    private IEnumerable<string>? _userGroups;

    public AuthInfo(IHttpContextAccessor httpContextAccessor)
    {
        this._data = new Lazy<AuthData>(() =>
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user is null)
            {
                throw new InvalidOperationException("HttpContext must be present to inspect auth info.");
            }
            var userIdClaim = user.FindFirst(ClaimConstants.Oid)
                ?? user.FindFirst(ClaimConstants.ObjectId)
                ?? user.FindFirst(ClaimConstants.Sub)
                ?? user.FindFirst(ClaimConstants.NameIdentifierId);
            if (userIdClaim is null)
            {
                throw new CredentialUnavailableException("User Id was not present in the request token.");
            }
            var tenantIdClaim = user.FindFirst(ClaimConstants.Tid)
                ?? user.FindFirst(ClaimConstants.TenantId);
            var userNameClaim = user.FindFirst(ClaimConstants.Name);
            if (userNameClaim is null)
            {
                throw new CredentialUnavailableException("User name was not present in the request token.");
            }

            if (tenantIdClaim is not null)
            {
                // get graph auth header - x-sk-copilot-graph-auth
                var graphAuthHeader = httpContextAccessor.HttpContext?.Request.Headers["x-sk-copilot-graph-auth"];
                if (graphAuthHeader is not null)
                {
                    // get graph extension
                    this._graphExtension = new GraphExtension(graphAuthHeader!);
                }
            }

            return new AuthData
            {
                // UserId = (tenantIdClaim is null) ? userIdClaim.Value : string.Join(".", userIdClaim.Value, tenantIdClaim.Value),
                UserId = userIdClaim.Value,
                UserName = userNameClaim.Value,
            };
        }, isThreadSafe: false);
    }

    /// <summary>
    /// The authenticated user's unique ID.
    /// </summary>
    public string UserId => this._data.Value.UserId;

    /// <summary>
    /// The authenticated user's name.
    /// </summary>
    public string Name => this._data.Value.UserName;

    /// <summary>
    /// The authenticated user's groups.
    /// </summary>
    /// custom getter method
    public IEnumerable<string>? UserGroups
    {
        get
        {
            if (this.UserId is null)
            {
                return null;
            }

            if (this._graphExtension is null)
            {
                return null;
            }

            if (this._userGroups is null)
            {
                var userGroups = this._graphExtension.GetUserGroupsAsync(this.UserId).Result;
                var groups = new List<string>();
                if (userGroups is not null)
                {
                    groups.AddRange(userGroups.Select(g => g.Id));
                }
                this._userGroups = groups.ToArray();
            }
            return this._userGroups;
        }
    }

    /// <summary>
    /// The authenticated user's graph extension.
    /// </summary>
    public GraphExtension? GraphExtension => this._graphExtension;

    /// <summary>
    /// Check that scopeId is present in the authenticated user's groups.
    /// </summary>
    public bool IsInScope(string scopeId)
    {
        if (this.UserId is null)
        {
            throw new CredentialUnavailableException("User Id was not present in the request token.");
        }
        if (!string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentException("Scope ID cannot be null or empty.", nameof(scopeId));
        }

        if (this.UserId == scopeId)
        {
            return true;
        }
        return this.UserGroups?.Contains(scopeId) ?? false;
    }

    /// <summary>
    /// Check that any scopeId present in the authenticated user's groups.
    /// </summary>
    public bool IsInAnyScope(IEnumerable<string> scopeIds)
    {
        if (this.UserId is null)
        {
            throw new CredentialUnavailableException("User Id was not present in the request token.");
        }
        ArgumentNullException.ThrowIfNull(scopeIds);

        return scopeIds.Any(this.IsInScope);
    }

    /// <summary>
    /// Check that all scopeIds are present in the authenticated user's groups.
    /// </summary>
    public bool IsInAllScopes(IEnumerable<string> scopeIds)
    {
        if (this.UserId is null)
        {
            throw new CredentialUnavailableException("User Id was not present in the request token.");
        }
        ArgumentNullException.ThrowIfNull(scopeIds);

        return scopeIds.All(this.IsInScope);
    }
}
