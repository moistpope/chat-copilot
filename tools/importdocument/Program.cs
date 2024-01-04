// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace ImportDocument;

/// <summary>
/// This console app imports a list of files to Chat Copilot's WebAPI document memory store.
/// </summary>
public static class Program
{
    private static string? _accessToken;
    private static string? _graphToken;
    private static readonly int _maxFiles = 100;
    private static readonly int _maxBytes = 80 * 1024 * 1024;

    private static readonly List<string> _supportedFileTypes = new()
    {
        ".txt",
        ".pdf",
        ".docx",
        ".md",
        ".jpg",
        ".jpeg",
        ".png",
        ".tif",
        ".tiff",
        ".bmp",
        ".gif"
    };

    public static void Main(string[] args)
    {
        var config = Config.GetConfig();
        if (!Config.Validate(config))
        {
            Console.WriteLine("Error: Failed to read appsettings.json.");
            return;
        }

        var filesOption = new Option<IEnumerable<FileInfo>>(name: "--files", description: "The files to import to document memory store.")
        {
            AllowMultipleArgumentsPerToken = true,
        };

        var foldersOption = new Option<IEnumerable<DirectoryInfo>>(name: "--folders", description: "The folders to import to document memory store.")
        {
            AllowMultipleArgumentsPerToken = true,
        };

        var scopeCollectionOption = new Option<IEnumerable<Guid>>(
            name: "--scope-id",
            description: "Save the extracted context with specified scope id permission.",
            getDefaultValue: () => new List<Guid> { Guid.Empty }
        )
        {
            AllowMultipleArgumentsPerToken = true,
        };

        var rootCommand = new RootCommand(
            "This console app imports files to Chat Copilot's WebAPI document memory store."
        )
        {
            filesOption,
            foldersOption,
            scopeCollectionOption
        };

        rootCommand.SetHandler(async (files, folders, scopeCollection) =>
            {
                await FileImportHandlerAsync(files, folders, config!, scopeCollection);
            },
            filesOption,
            foldersOption,
            scopeCollectionOption
        );

        if (config?.AuthenticationType == "AzureAd")
        {
            if (!AcquireTokenAsync(config).Result)
            {
                return;
            }
            if (!AcquireGraphTokenAsync(config).Result)
            {
                return;
            }
        }

        rootCommand.Invoke(args);
    }

    /// <summary>
    /// Acquires a user account from Azure AD.
    /// </summary>
    /// <param name="config">The App configuration.</param>
    /// <param name="setAccessToken">Sets the access token to the first account found.</param>
    /// <returns>True if the user account was acquired.</returns>
    private static async Task<bool> AcquireTokenAsync(
        Config config)
    {
        Console.WriteLine("Attempting to authenticate user...");

        var webApiScope = $"api://{config.BackendClientId}/{config.Scopes}";
        string[] scopes = { webApiScope, "openid profile User.Read email" };
        try
        {
            var app = PublicClientApplicationBuilder.Create(config.ClientId)
                .WithRedirectUri(config.RedirectUri)
                .WithAuthority(config.Instance, config.TenantId)
                .Build();
            var result = await app.AcquireTokenInteractive(scopes).ExecuteAsync();
            Program._accessToken = result.AccessToken;
            return true;
        }
        catch (Exception ex) when (ex is MsalServiceException or MsalClientException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }

    //AcquireGraphTokenAsync
    private static async Task<bool> AcquireGraphTokenAsync(Config config)
    {
        Console.WriteLine("Attempting to authenticate user...");

        string[] scopes = { "openid profile User.Read email" };
        try
        {
            var app = PublicClientApplicationBuilder.Create(config.ClientId)
                .WithRedirectUri(config.RedirectUri)
                .WithAuthority(config.Instance, config.TenantId)
                .Build();
            var result = await app.AcquireTokenInteractive(scopes).ExecuteAsync();
            Program._graphToken = result.AccessToken;
            return true;
        }
        catch (Exception ex) when (ex is MsalServiceException or MsalClientException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }
    /// <summary>
    /// Conditionally imports a list of files to the Document Store.
    /// </summary>
    /// <param name="files">A list of files to import.</param>
    /// <param name="config">Configuration.</param>
    /// <param name="chatCollectionId">Save the extracted context to an isolated chat collection.</param>
    private static async Task ImportFilesAsync(IEnumerable<FileInfo> files, Config config, IEnumerable<Guid> scopeIds)
    {
        foreach (var file in files)
        {
            if (!file.Exists)
            {
                Console.WriteLine($"File {file.FullName} does not exist.");
                return;
            }
        }

        using var formContent = new MultipartFormDataContent();
        List<StreamContent> filesContent = files.Select(file => new StreamContent(file.OpenRead())).ToList();
        for (int i = 0; i < filesContent.Count; i++)
        {
            formContent.Add(filesContent[i], "formFiles", files.ElementAt(i).Name);
        }

        List<StringContent> scopeIdsContent = scopeIds.Select(scopeId => new StringContent(scopeId.ToString())).ToList();
        for (int i = 0; i < scopeIdsContent.Count; i++)
        {
            formContent.Add(scopeIdsContent[i], "ScopeIds");
        }

        Console.WriteLine($"Scopes: {string.Join(", ", scopeIds)}");

        Console.WriteLine($"Uploading and parsing file with scopes to global collection...");
        await UploadAsync();

        // Dispose of all the file streams.
        foreach (var fileContent in filesContent)
        {
            fileContent.Dispose();
        }

        async Task UploadAsync()
        {
            // Create a HttpClient instance and set the timeout to infinite since
            // large documents will take a while to parse.
            using HttpClientHandler clientHandler = new()
            {
                CheckCertificateRevocationList = true
            };
            using HttpClient httpClient = new(clientHandler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            if (config.AuthenticationType == "AzureAd")
            {
                // Add required properties to the request header.
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {Program._accessToken!}");
                httpClient.DefaultRequestHeaders.Add("X-Sk-Copilot-Graph-Auth", Program._graphToken!);
            }
            else if (config.AuthenticationType == "ApiKey")
            {
            }

            string uriPath = "documents";

            try
            {
                using HttpResponseMessage response = await httpClient.PostAsync(
                    new Uri(new Uri(config.ServiceUri), uriPath),
                    formContent);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: {response.StatusCode} {response.ReasonPhrase}");
                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                    return;
                }

                Console.WriteLine("Uploading and parsing successful.");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    // find all perms.txt files, read the scope ids, and map them to the files and folders in the same or lower directory
    // if a file or folder has a perms.txt file, it should override the parent perms.txt file
    private static async Task FileImportHandlerAsync(IEnumerable<FileInfo> argFiles, IEnumerable<DirectoryInfo> argFolders, Config config, IEnumerable<Guid> argScopeIds)
    {

        if (argFiles.Any())
        {
            await ImportFilesAsync(argFiles, config, argScopeIds);
        }

        if (!argFolders.Any())
        {
            return;
        }

        foreach (var folderFromArguments in argFolders)
        {
            if (!folderFromArguments.Exists)
            {
                Console.WriteLine($"Folder {folderFromArguments.FullName} does not exist.");
                return;
            }
            if (folderFromArguments.GetFiles("*.perms").Length == 0)
            {
                Console.WriteLine($"Folder {folderFromArguments.FullName} does not contain a .perms files.");
                return;
            }

            var folders = GetFoldersInDirectory(folderFromArguments.FullName).Select(folder => (folder, GetScopeIds(folder, folderFromArguments.FullName))).ToList();
            folders.Add((folderFromArguments.FullName, GetScopeIds(folderFromArguments.FullName, folderFromArguments.FullName)));

            foreach (var folderKv in folders)
            {
                var folder = folderKv.folder;
                var scopeIds = folderKv.Item2;
                var filesInFolder = GetFilesInDirectory(folder);
                var files = filesInFolder.Select(file => new FileInfo(file)).ToList();
                // send a max of 80MB or 100 files at a time
                var filesToSend = new List<FileInfo>();
                var bytesToSend = 0;
                foreach (var file in files)
                {
                    if (filesToSend.Count >= _maxFiles || bytesToSend + file.Length >= _maxBytes)
                    {
                        await ImportFilesAsync(filesToSend, config, scopeIds.Select(scopeId => Guid.Parse(scopeId)));
                        filesToSend.Clear();
                        bytesToSend = 0;
                    }
                    filesToSend.Add(file);
                    bytesToSend += (int)file.Length;
                }

                await ImportFilesAsync(files, config, scopeIds.Select(scopeId => Guid.Parse(scopeId)));
            }
        }
    }

    private static List<string> GetScopeIds(string folder, string rootFolder)
    {
        var scopeIds = new List<string>();
        var permsFiles = Directory.GetFiles(folder, "*.perms", SearchOption.TopDirectoryOnly);

        if (permsFiles.Length == 0)
        {
            var parentFolder = Directory.GetParent(folder);
            // ensure parentFolder isn't null and isn't higher than rootFolder
            while (parentFolder != null && parentFolder.FullName != rootFolder)
            {
                permsFiles = Directory.GetFiles(parentFolder.FullName, "*.perms", SearchOption.TopDirectoryOnly);
                if (permsFiles.Length > 0)
                {
                    break;
                }
                parentFolder = Directory.GetParent(parentFolder.FullName);
            }

            if (permsFiles.Length == 0)
            {
                permsFiles = Directory.GetFiles(rootFolder, "*.perms", SearchOption.TopDirectoryOnly);
            }
        }

        foreach (var permsFile in permsFiles)
        {
            var scopeIdsFromFile = File.ReadAllLines(permsFile);
            scopeIds.AddRange(scopeIdsFromFile);
        }
        return scopeIds;
    }

    private static List<string> GetFilesInDirectory(string path)
    {
        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();
        // remove any files that begin with special characters
        files.RemoveAll(file => Path.GetFileName(file).StartsWith("~"));
        files.RemoveAll(file => Path.GetFileName(file).StartsWith("$"));
        files.RemoveAll(file => Path.GetFileName(file).StartsWith("."));
        files.RemoveAll(file => !_supportedFileTypes.Contains(Path.GetExtension(file)));
        return files;
    }

    private static List<string> GetFoldersInDirectory(string path)
    {
        return Directory.GetDirectories(path, "*.*", SearchOption.AllDirectories).ToList();
    }

}
