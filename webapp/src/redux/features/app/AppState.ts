// Copyright (c) Microsoft. All rights reserved.

import { AuthConfig } from '../../../libs/auth/AuthHelper';
import { AlertType } from '../../../libs/models/AlertType';
import { IChatUser } from '../../../libs/models/ChatUser';
import { ServiceInfo } from '../../../libs/models/ServiceInfo';
import { TokenUsage } from '../../../libs/models/TokenUsage';

// This is the default user information when authentication is set to 'None'.
// It must match what is defined in PassthroughAuthenticationHandler.cs on the backend.
export const DefaultChatUser: IChatUser = {
    id: 'c05c61eb-65e4-4223-915a-fe72b0c9ece1',
    emailAddress: 'user@contoso.com',
    fullName: 'Default User',
    online: true,
    isTyping: false,
};

export const DefaultActiveUserInfo: ActiveUserInfo = {
    id: DefaultChatUser.id,
    email: DefaultChatUser.emailAddress,
    username: DefaultChatUser.fullName,
};

export interface ActiveUserInfo {
    id: string;
    email: string;
    username: string;
    graphUserData?: GraphUserData;
    graphUserGroups?: GraphUserGroupData[];
}

export interface GraphUserData {
    id: string;
    displayName: string;
    givenName?: string;
    surname?: string;
    jobTitle?: string;
    mail: string;
    mobilePhone?: string;
    officeLocation?: string;
    preferredLanguage?: string;
    userPrincipalName?: string;
    businessPhones?: string[];
}

export interface GraphUserGroupData {
    id: string;
    displayName: string;
    description?: string;
    mail?: string;
    mailEnabled?: boolean;
    mailNickname?: string;
    securityEnabled?: boolean;
    visibility?: string;
}

export interface Alert {
    message: string;
    type: AlertType;
    id?: string;
    onRetry?: () => void;
}

interface Feature {
    enabled: boolean; // Whether to show the feature in the UX
    label: string;
    inactive?: boolean; // Set to true if you don't want the user to control the visibility of this feature or there's no backend support
    description?: string;
}

export interface Setting {
    title: string;
    description?: string;
    features: FeatureKeys[];
    stackVertically?: boolean;
    learnMoreLink?: string;
}

export interface AppState {
    alerts: Alert[];
    activeUserInfo?: ActiveUserInfo;
    authConfig?: AuthConfig | null;
    tokenUsage: TokenUsage;
    features: Record<FeatureKeys, Feature>;
    settings: Setting[];
    serviceInfo: ServiceInfo;
    isMaintenance: boolean;
}

export enum FeatureKeys {
    DarkMode,
    SimplifiedExperience,
    PluginsPlannersAndPersonas,
    AzureContentSafety,
    AzureCognitiveSearch,
    BotAsDocs,
    MultiUserChat,
    RLHF,
}

export const Features = {
    [FeatureKeys.DarkMode]: {
        enabled: true,
        label: 'Dark Mode',
    },
    [FeatureKeys.SimplifiedExperience]: {
        enabled: false,
        label: 'Simplified Chat Experience',
    },
    [FeatureKeys.PluginsPlannersAndPersonas]: {
        enabled: true,
        label: 'Plugins & Planners & Personas',
        description: 'The Plans and Persona tabs are hidden until you turn this on',
    },
    [FeatureKeys.AzureContentSafety]: {
        enabled: false,
        label: 'Azure Content Safety',
        inactive: true,
    },
    [FeatureKeys.AzureCognitiveSearch]: {
        enabled: true,
        label: 'Azure Cognitive Search',
        inactive: false,
    },
    [FeatureKeys.BotAsDocs]: {
        enabled: false,
        label: 'Export Chat Sessions',
    },
    [FeatureKeys.MultiUserChat]: {
        enabled: false,
        label: 'Live Chat Session Sharing',
        description: 'Enable multi-user chat sessions. Not available when authorization is disabled.',
    },
    [FeatureKeys.RLHF]: {
        enabled: true,
        label: 'Reinforcement Learning from Human Feedback',
        description: 'Enable users to vote on model-generated responses. For demonstration purposes only.',
        // TODO: [Issue #42] Send and store feedback in backend
    },
};

export const Settings = [
    {
        // Basic settings has to stay at the first index. Add all new settings to end of array.
        title: 'Basic',
        features: [FeatureKeys.DarkMode, FeatureKeys.PluginsPlannersAndPersonas],
        stackVertically: true,
    },
    {
        title: 'Display',
        features: [FeatureKeys.SimplifiedExperience],
        stackVertically: true,
    },
    {
        title: 'Azure AI',
        features: [FeatureKeys.AzureContentSafety, FeatureKeys.AzureCognitiveSearch],
        stackVertically: true,
    },
    {
        title: 'Experimental',
        description: 'The related icons and menu options are hidden until you turn this on',
        features: [FeatureKeys.BotAsDocs, FeatureKeys.MultiUserChat, FeatureKeys.RLHF],
    },
];

export const initialState: AppState = {
    alerts: [],
    activeUserInfo: DefaultActiveUserInfo,
    authConfig: {} as AuthConfig,
    tokenUsage: {},
    features: Features,
    settings: Settings,
    serviceInfo: {
        memoryStore: { types: [], selectedType: '' },
        availablePlugins: [],
        version: '',
        isContentSafetyEnabled: false,
    },
    isMaintenance: false,
};
