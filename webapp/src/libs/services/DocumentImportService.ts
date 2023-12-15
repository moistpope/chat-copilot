// Copyright (c) Microsoft. All rights reserved.

import { IChatMessage } from '../models/ChatMessage';
import { ServiceInfo } from '../models/ServiceInfo';
import { BaseService } from './BaseService';

export class DocumentImportService extends BaseService {
    public importDocumentAsync = async (
        chatId: string,
        documents: File[],
        useContentSafety: boolean,
        accessToken: string,
        uploadToChatOnly: boolean,
    ) => {
        const formData = new FormData();
        formData.append('useContentSafety', useContentSafety.toString());
        for (const document of documents) {
            formData.append('formFiles', document);
        }

        return await this.getResponseAsync<IChatMessage>(
            {
                commandPath: uploadToChatOnly ? `chats/${chatId}/documents` : `documents`,
                method: 'POST',
                body: formData,
            },
            accessToken,
        );
    };

    public getContentSafetyStatusAsync = async (accessToken: string): Promise<boolean> => {
        const serviceInfo = await this.getResponseAsync<ServiceInfo>(
            {
                commandPath: 'info',
                method: 'GET',
            },
            accessToken,
        );

        return serviceInfo.isContentSafetyEnabled;
    };
}
