// Copyright (c) Microsoft. All rights reserved.

import { GraphUserGroupData } from '../../redux/features/app/AppState';

export interface IGraphUserData {
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

export interface IGraphUserGroupResponse {
    value: GraphUserGroupData[];
}
