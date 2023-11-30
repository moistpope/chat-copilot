// Copyright (c) Microsoft. All rights reserved.

import { IGraphUserData } from './GraphUserData';

export interface IChatUser {
    id: string;
    online: boolean;
    fullName: string;
    emailAddress: string;
    photo?: string; // TODO: [Issue #45] change this to required when we enable token / Graph support
    isTyping: boolean;
    graphUserData?: IGraphUserData;
}
