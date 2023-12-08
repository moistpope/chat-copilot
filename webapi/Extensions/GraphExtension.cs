// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel.Functions.OpenAPI.Authentication;

namespace CopilotChat.WebApi.Extensions;

/// <summary>
/// Extension methods for retrieving data using MS Graph SDK.
/// </summary>
public class GraphExtension
{
    // class instance of GraphServiceClient that is created when class is initialized with a token
    private readonly GraphServiceClient _graphClient;

    public GraphExtension(IAuthenticationProvider authenticationProvider)
    {
        ArgumentNullException.ThrowIfNull(authenticationProvider, nameof(authenticationProvider));
        // this._logger.LogInformation("Enabling Microsoft Graph plugin(s).");
        IList<DelegatingHandler> graphMiddlewareHandlers = GraphClientFactory.CreateDefaultHandlers(authenticationProvider);

        using HttpClient graphHttpClient = GraphClientFactory.Create(graphMiddlewareHandlers);

        this._graphClient = new(graphHttpClient);
    }

    public GraphExtension(string token)
    {
        if (token.IsNullOrEmpty())
        {
            throw new ArgumentNullException(nameof(token));
        }
        // this._logger.LogInformation("Enabling Microsoft Graph plugin(s).");
        BearerAuthenticationProvider authenticationProvider = new(() => Task.FromResult(token));

        IList<DelegatingHandler> graphMiddlewareHandlers = GraphClientFactory.CreateDefaultHandlers(new DelegateAuthenticationProvider(authenticationProvider.AuthenticateRequestAsync));

        using HttpClient graphHttpClient = GraphClientFactory.Create(graphMiddlewareHandlers);

        this._graphClient = new(graphHttpClient);
    }

    // get user profile information
    /// <summary>
    /// Get the user's profile information.
    /// </summary>
    /// <param name="graphClient">The graph client.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The user's profile information.</returns>
    public async Task<User> GetUserProfileAsync(string userId)
    {
        return await this._graphClient.Users[userId].Request().GetAsync();
    }

    // get user groups 
    /// <summary>
    /// Get the user's groups.
    /// </summary>
    /// <param name="graphClient">The graph client.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The user's groups.</returns>
    public async Task<IEnumerable<Group>?> GetUserGroupsAsync(string userId)
    {
        var groups = await this._graphClient.Users[userId].MemberOf.Request().Select("id,displayName").GetAsync();
        var result = new List<Group>();
        var page = groups;
        while (page != null)
        {
            result.AddRange(page.CurrentPage.OfType<Group>());
            page = page.NextPageRequest != null ? await page.NextPageRequest.GetAsync() : null;
        }
        return result;
    }

    public async Task<IEnumerable<string>?> GetUserGroupIdsAsync(string userId)
    {
        var groups = await this.GetUserGroupsAsync(userId);
        return groups?.Select(g => g.Id);
    }

    /// <summary>
    /// Get the user's profile picture.
    /// </summary>
    /// <param name="graphClient">The graph client.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The user's profile picture.</returns>
    public async Task<byte[]> GetUserProfilePictureAsync(string userId)
    {
        var stream = await this._graphClient.Users[userId].Photo.Content.Request().GetAsync();
        var buffer = new byte[stream.Length];
        await stream.ReadAsync(buffer, 0, (int)stream.Length);
        return buffer;
    }

    /// <summary>
    /// Get the user's profile picture.
    /// </summary>
    /// <param name="graphClient">The graph client.</param>
    /// <param name="userIds">The user ids.</param>
    /// <returns>The user's profile picture.</returns>
    public async Task<Dictionary<string, byte[]>> GetUserProfilePicturesAsync(IEnumerable<string> userIds)
    {
        var result = new Dictionary<string, byte[]>();
        foreach (var userId in userIds)
        {
            var stream = await this._graphClient.Users[userId].Photo.Content.Request().GetAsync();
            var buffer = new byte[stream.Length];
            await stream.ReadAsync(buffer, 0, (int)stream.Length);
            result.Add(userId, buffer);
        }

        return result;
    }
}
