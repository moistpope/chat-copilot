﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CopilotChat.WebApi.Auth;
using CopilotChat.WebApi.Extensions;
using CopilotChat.WebApi.Hubs;
using CopilotChat.WebApi.Models.Request;
using CopilotChat.WebApi.Models.Response;
using CopilotChat.WebApi.Models.Storage;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Services;
using CopilotChat.WebApi.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DataFormats.Pdf;

namespace CopilotChat.WebApi.Controllers;

/// <summary>
/// Controller for importing documents.
/// </summary>
/// <remarks>
/// This controller is responsible for contracts that are not possible to fulfill by kernel memory components.
/// </remarks>
[ApiController]
public class DocumentController : ControllerBase
{
    private const string GlobalDocumentUploadedClientCall = "GlobalDocumentUploaded";
    private const string ReceiveMessageClientCall = "ReceiveMessage";

    private readonly ILogger<DocumentController> _logger;
    private readonly PromptsOptions _promptOptions;
    private readonly DocumentMemoryOptions _options;
    private readonly ContentSafetyOptions _contentSafetyOptions;
    private readonly ChatSessionRepository _sessionRepository;
    private readonly ChatMemorySourceRepository _sourceRepository;
    private readonly ChatMessageRepository _messageRepository;
    private readonly ChatParticipantRepository _participantRepository;
    private readonly DocumentTypeProvider _documentTypeProvider;
    private readonly IAuthInfo _authInfo;
    private readonly IContentSafetyService _contentSafetyService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentImportController"/> class.
    /// </summary>
    public DocumentController(
        ILogger<DocumentController> logger,
        IAuthInfo authInfo,
        IOptions<DocumentMemoryOptions> documentMemoryOptions,
        IOptions<PromptsOptions> promptOptions,
        IOptions<ContentSafetyOptions> contentSafetyOptions,
        ChatSessionRepository sessionRepository,
        ChatMemorySourceRepository sourceRepository,
        ChatMessageRepository messageRepository,
        ChatParticipantRepository participantRepository,
        DocumentTypeProvider documentTypeProvider,
        IContentSafetyService contentSafetyService)
    {
        this._logger = logger;
        this._options = documentMemoryOptions.Value;
        this._promptOptions = promptOptions.Value;
        this._contentSafetyOptions = contentSafetyOptions.Value;
        this._sessionRepository = sessionRepository;
        this._sourceRepository = sourceRepository;
        this._messageRepository = messageRepository;
        this._participantRepository = participantRepository;
        this._documentTypeProvider = documentTypeProvider;
        this._authInfo = authInfo;
        this._contentSafetyService = contentSafetyService;
    }

    /// <summary>
    /// Service API for importing a document.
    /// Documents imported through this route will scoped by documentImportForm.ScopeIds.
    /// </summary>
    [Route("documents")]
    [HttpPost]
    [RequestSizeLimit(100000000)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> DocumentImportAsync(
        [FromServices] IKernelMemory memoryClient,
        [FromServices] IHubContext<MessageRelayHub> messageRelayHubContext,
        [FromForm] DocumentImportForm documentImportForm)
    {
        // print documentImportForm.ScopeIds
        Console.WriteLine($"documentImportForm.ScopeIds: {string.Join(", ", documentImportForm.ScopeIds)}");
        return this.DocumentImportAsync(
            memoryClient,
            messageRelayHubContext,
            documentImportForm,
            null
        );
    }

    /// <summary>
    /// Service API for importing a document.
    /// </summary>
    [Route("chats/{chatId}/documents")]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> DocumentImportAsync(
        [FromServices] IKernelMemory memoryClient,
        [FromServices] IHubContext<MessageRelayHub> messageRelayHubContext,
        [FromRoute] string chatId,
        [FromForm] DocumentImportForm documentImportForm)
    {
        return this.DocumentImportAsync(
            memoryClient,
            messageRelayHubContext,
            documentImportForm,
            chatId);
    }

    private async Task<IActionResult> DocumentImportAsync(
        IKernelMemory memoryClient,
        IHubContext<MessageRelayHub> messageRelayHubContext,
        DocumentImportForm documentImportForm,
        string? chatId = null)
    {
        try
        {
            await this.ValidateDocumentImportFormAsync(this._authInfo.UserId, documentImportForm, chatId);
        }
        catch (ArgumentException ex)
        {
            return this.BadRequest(ex.Message);
        }

        this._logger.LogInformation("Importing {0} document(s)...", documentImportForm.FormFiles.Count());

        // Pre-create chat-message
        DocumentMessageContent documentMessageContent = new();

        this._logger.LogInformation("Testing for zero-length PDF files...");

        foreach (var formFile in documentImportForm.FormFiles)
        {
            if (formFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var text = new PdfDecoder().DocToText(formFile.OpenReadStream()).ToString().Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Length == 0)
                {
                    this._logger.LogWarning("Zero-length PDF file detected: {0}", formFile.FileName);
                    documentImportForm.FormFiles = documentImportForm.FormFiles.Where(file => file.FileName != formFile.FileName);
                }
            }
            if (formFile.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                this._logger.LogInformation("docx file detected: {0}", formFile.FileName);
            }
        }

        var importResults = await this.ImportDocumentsAsync(memoryClient, documentImportForm, documentMessageContent);

        if (!string.IsNullOrEmpty(chatId))
        {
            var chatMessage = await this.TryCreateDocumentUploadMessage(chatId, documentMessageContent);

            if (chatMessage == null)
            {
                this._logger.LogWarning("Failed to create document upload message - {Content}", documentMessageContent.ToString());
                return this.BadRequest();
            }

            var userId = this._authInfo.UserId;
            await messageRelayHubContext.Clients.Group(chatId)
                .SendAsync(ReceiveMessageClientCall, chatId, userId, chatMessage);

            this._logger.LogInformation("Local upload chat message: {0}", chatMessage.ToString());

            return this.Ok(chatMessage);
        }
        else
        {
            var chatMessage = await this.TryCreateDocumentUploadMessage(Guid.Empty.ToString(), documentMessageContent);

            if (chatMessage == null)
            {
                this._logger.LogWarning("Failed to create document upload message - {Content}", documentMessageContent.ToString());
                return this.BadRequest();
            }

            await messageRelayHubContext.Clients.All.SendAsync(
            GlobalDocumentUploadedClientCall,
            documentMessageContent.ToFormattedStringNamesOnly(),
            this._authInfo.Name
        );

            this._logger.LogInformation("Global upload chat message: {0}", chatMessage.ToString());

            return this.Ok(chatMessage);
        }
    }

    private async Task<IList<ImportResult>> ImportDocumentsAsync(IKernelMemory memoryClient, DocumentImportForm documentImportForm, DocumentMessageContent messageContent)
    {
        IEnumerable<ImportResult> importResults = new List<ImportResult>();

        await Task.WhenAll(
            documentImportForm.FormFiles.Select(
                async formFile =>
                    await this.ImportDocumentAsync(formFile, memoryClient, documentImportForm.ScopeIds.ToArray(), this._authInfo.UserId).ContinueWith(
                        task =>
                        {
                            var importResult = task.Result;
                            if (importResult != null)
                            {
                                messageContent.AddDocument(
                                    formFile.FileName,
                                    this.GetReadableByteString(formFile.Length),
                                    importResult.IsSuccessful);

                                importResults = importResults.Append(importResult);
                            }
                        },
                        TaskScheduler.Default)));

        return importResults.ToArray();
    }

    private async Task<ImportResult> ImportDocumentAsync(IFormFile formFile, IKernelMemory memoryClient, string[] scopeIds, string userId)
    {
        this._logger.LogInformation("Importing document {0}", formFile.FileName);

        var fileName = RemoveSpecialCharacters(Path.GetFileNameWithoutExtension(formFile.FileName));
        var fileExtension = Path.GetExtension(formFile.FileName);
        var sanitizedFileName = $"{fileName}{fileExtension}";

        // Create memory source
        MemorySource memorySource = new(
            scopeIds,
            sanitizedFileName,
            userId,
            MemorySourceType.File,
            userId,
            formFile.Length,
            hyperlink: null
        );

        // if (!await this.TryUpsertMemorySourceAsync(memorySource))
        // {
        //     this._logger.LogDebug("Failed to upsert memory source for file {0}.", formFile.FileName);

        //     return ImportResult.Fail;
        // }

        if (!await TryStoreMemoryAsync())
        {
            // await this.TryRemoveMemoryAsync(memorySource);
            throw new Exception($"Failed to store document {formFile.FileName}.");
        }

        return new ImportResult(memorySource.Id);

        async Task<bool> TryStoreMemoryAsync()
        {
            try
            {
                using var stream = formFile.OpenReadStream();
                await memoryClient.StoreDocumentAsync(
                    this._promptOptions.MemoryIndexName,
                    memorySource.Id,
                    scopeIds,
                    userId,
                    this._promptOptions.DocumentMemoryName,
                    sanitizedFileName,
                    stream);

                return true;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to store document {0}. Details: {{1}}", formFile.FileName, ex.Message);
                throw new Exception($"Failed to store document {formFile.FileName}.", ex);
            }
        }

        static string RemoveSpecialCharacters(string str)
        {
            StringBuilder sb = new();
            foreach (char c in str)
            {
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }

    #region Private

    /// <summary>
    /// A class to store a document import results.
    /// </summary>
    private sealed class ImportResult
    {
        /// <summary>
        /// A boolean indicating whether the import is successful.
        /// </summary>
        public bool IsSuccessful => !string.IsNullOrWhiteSpace(this.CollectionName);

        /// <summary>
        /// The name of the collection that the document is inserted to.
        /// </summary>
        public string CollectionName { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="ImportResult"/> class.
        /// </summary>
        /// <param name="collectionName">The name of the collection that the document is inserted to.</param>
        public ImportResult(string collectionName)
        {
            this.CollectionName = collectionName;
        }

        /// <summary>
        /// Create a new instance of the <see cref="ImportResult"/> class representing a failed import.
        /// </summary>
        public static ImportResult Fail { get; } = new(string.Empty);
    }

    /// <summary>
    /// Validates the document import form.
    /// </summary>
    /// <param name="documentImportForm">The document import form.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Throws ArgumentException if validation fails.</exception>
    private async Task ValidateDocumentImportFormAsync(string userId, DocumentImportForm documentImportForm, string? chatId = null)
    {
        var scopeIds = documentImportForm.ScopeIds;

        if (chatId != null)
        {
            scopeIds = scopeIds.Append(chatId).ToList();
        }

        // Make sure the user has access to the chat session if the document is uploaded to a chat session.
        if (!await this.UserHasAccessToAllScopesAsync(this._authInfo, scopeIds.ToArray()))
        {
            throw new ArgumentException("User does not have access to the chat session.");
        }

        var formFiles = documentImportForm.FormFiles;

        if (!formFiles.Any())
        {
            throw new ArgumentException("No files were uploaded.");
        }
        else if (formFiles.Count() > this._options.FileCountLimit)
        {
            throw new ArgumentException($"Too many files uploaded. Max file count is {this._options.FileCountLimit}.");
        }

        // Loop through the uploaded files and validate them before importing.
        foreach (var formFile in formFiles)
        {
            if (formFile.Length == 0)
            {
                throw new ArgumentException($"File {formFile.FileName} is empty.");
            }

            if (formFile.Length > this._options.FileSizeLimit)
            {
                throw new ArgumentException($"File {formFile.FileName} size exceeds the limit.");
            }

            // Make sure the file type is supported.
            var fileType = Path.GetExtension(formFile.FileName);
            if (!this._documentTypeProvider.IsSupported(fileType, out bool isSafetyTarget))
            {
                throw new ArgumentException($"Unsupported file type: {fileType}");
            }

            if (isSafetyTarget && documentImportForm.UseContentSafety)
            {
                if (!this._contentSafetyOptions.Enabled)
                {
                    throw new ArgumentException("Unable to analyze image. Content Safety is currently disabled in the backend.");
                }

                var violations = new List<string>();
                try
                {
                    // Call the content safety controller to analyze the image
                    var imageAnalysisResponse = await this._contentSafetyService.ImageAnalysisAsync(formFile, default);
                    violations = this._contentSafetyService.ParseViolatedCategories(imageAnalysisResponse, this._contentSafetyOptions.ViolationThreshold);
                }
                catch (Exception ex) when (!ex.IsCriticalException())
                {
                    this._logger.LogError(ex, "Failed to analyze image {0} with Content Safety. Details: {{1}}", formFile.FileName, ex.Message);
                    throw new AggregateException($"Failed to analyze image {formFile.FileName} with Content Safety.", ex);
                }

                if (violations.Count > 0)
                {
                    throw new ArgumentException($"Unable to upload image {formFile.FileName}. Detected undesirable content with potential risk: {string.Join(", ", violations)}");
                }
            }
        }
    }

    /// <summary>
    /// Validates the document import form.
    /// </summary>
    /// <param name="documentStatusForm">The document import form.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Throws ArgumentException if validation fails.</exception>
    private async Task ValidateDocumentStatusFormAsync(DocumentStatusForm documentStatusForm)
    {
        // Make sure the user has access to the chat session if the document is uploaded to a chat session.
        if (!await this.UserHasAccessToAllScopesAsync(this._authInfo, (string[])documentStatusForm.ScopeIds))
        {
            throw new ArgumentException("User does not have access to the chat session.");
        }

        var fileReferences = documentStatusForm.FileReferences;

        if (!fileReferences.Any())
        {
            throw new ArgumentException("No files identified.");
        }
        else if (fileReferences.Count() > this._options.FileCountLimit)
        {
            throw new ArgumentException($"Too many files requested. Max file count is {this._options.FileCountLimit}.");
        }

        // Loop through the uploaded files and validate them before importing.
        foreach (var fileReference in fileReferences)
        {
            if (string.IsNullOrWhiteSpace(fileReference))
            {
                throw new ArgumentException($"File {fileReference} is empty.");
            }
        }
    }

    /// <summary>
    /// Try to upsert a memory source.
    /// </summary>
    /// <param name="memorySource">The memory source to be uploaded</param>
    /// <returns>True if upsert is successful. False otherwise.</returns>
    private async Task<bool> TryUpsertMemorySourceAsync(MemorySource memorySource)
    {
        try
        {
            await this._sourceRepository.UpsertAsync(memorySource);
            return true;
        }
        catch (Exception ex) when (ex is not SystemException)
        {
            return false;
        }
    }

    /// <summary>
    /// Try to upsert a memory source.
    /// </summary>
    /// <param name="memorySource">The memory source to be uploaded</param>
    /// <returns>True if upsert is successful. False otherwise.</returns>
    private async Task<bool> TryRemoveMemoryAsync(MemorySource memorySource)
    {
        try
        {
            await this._sourceRepository.DeleteAsync(memorySource);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Try to upsert a memory source.
    /// </summary>
    /// <param name="memorySource">The memory source to be uploaded</param>
    /// <returns>True if upsert is successful. False otherwise.</returns>
    private async Task<bool> TryStoreMemoryAsync(MemorySource memorySource)
    {
        try
        {
            await this._sourceRepository.UpsertAsync(memorySource);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Try to create a chat message that represents document upload.
    /// </summary>
    /// <param name="chatId">The target chat-id</param>
    /// <param name="messageContent">The document message content</param>
    /// <returns>A ChatMessage object if successful, null otherwise</returns>
    private async Task<CopilotChatMessage?> TryCreateDocumentUploadMessage(
        string chatId,
        DocumentMessageContent messageContent)
    {
        var chatMessage = CopilotChatMessage.CreateDocumentMessage(
            this._authInfo.UserId,
            this._authInfo.Name, // User name
            chatId,
            messageContent);

        try
        {
            await this._messageRepository.CreateAsync(chatMessage);
            return chatMessage;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a `long` byte count to a human-readable string.
    /// </summary>
    /// <param name="bytes">Byte count</param>
    /// <returns>Human-readable string of bytes</returns>
    private string GetReadableByteString(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblsBytes = bytes;
        for (i = 0; i < sizes.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblsBytes = bytes / 1024.0;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.#}{1}", dblsBytes, sizes[i]);
    }

    /// <summary>
    /// Check if the user has access to all scopeIds
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="scopeIds">The scope IDs.</param>
    /// <returns>A boolean indicating whether the user has access to all scopeIds.</returns>
    private async Task<bool> UserHasAccessToAllScopesAsync(IAuthInfo user, IEnumerable<string> scopeIds)
    {
        var scopeIdsArray = scopeIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();

        var excludedValues = new string[] { Guid.Empty.ToString(), user.UserId };
        if (scopeIdsArray.Length == 1 && (scopeIdsArray.First() == Guid.Empty.ToString() || scopeIdsArray.First() == user.UserId))
        {
            return true;
        }

        var notInGroups = scopeIdsArray.Except(excludedValues).ToList();
        if (user.UserGroups != null)
        {
            notInGroups = notInGroups.Except(user.UserGroups).ToList();
        }

        // return true if user is in all chatIds
        // global, user id, all user groups have been removed, so the only thing left should be chatIds
        return await Task.Run(() => notInGroups.All(id => this._participantRepository.IsUserInChatAsync(user.UserId, id).Result));
    }
    #endregion
}
