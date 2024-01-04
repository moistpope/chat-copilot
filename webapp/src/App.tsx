// Copyright (c) Microsoft. All rights reserved.

import { AuthenticatedTemplate, UnauthenticatedTemplate, useIsAuthenticated, useMsal } from '@azure/msal-react';
import { FluentProvider, Subtitle1, makeStyles, shorthands, tokens } from '@fluentui/react-components';

import * as React from 'react';
import { useEffect } from 'react';
import { UserSettingsMenu } from './components/header/UserSettingsMenu';
import { PluginGallery } from './components/open-api-plugins/PluginGallery';
import { BackendProbe, ChatView, Error, Loading, Login } from './components/views';
import { AuthHelper } from './libs/auth/AuthHelper';
import { TokenHelper } from './libs/auth/TokenHelper';
import { useChat, useFile, useGraph } from './libs/hooks';
import { AlertType } from './libs/models/AlertType';
import { useAppDispatch, useAppSelector } from './redux/app/hooks';
import { RootState } from './redux/app/store';
import { FeatureKeys } from './redux/features/app/AppState';
import { addAlert, setActiveUserInfo, setServiceInfo } from './redux/features/app/appSlice';
import { BuiltInPlugins, initialState } from './redux/features/plugins/PluginsState';
import { connectPlugin } from './redux/features/plugins/pluginsSlice';
import { semanticKernelDarkTheme, semanticKernelLightTheme } from './styles';

export const useClasses = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100vh',
        width: '100%',
        ...shorthands.overflow('hidden'),
    },
    header: {
        alignItems: 'center',
        backgroundColor: tokens.colorBrandForeground2,
        color: tokens.colorNeutralForegroundOnBrand,
        display: 'flex',
        '& h1': {
            paddingLeft: tokens.spacingHorizontalXL,
            display: 'flex',
        },
        height: '48px',
        justifyContent: 'space-between',
        width: '100%',
    },
    persona: {
        marginRight: tokens.spacingHorizontalXXL,
    },
    cornerItems: {
        display: 'flex',
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
});

enum AppState {
    ProbeForBackend,
    SettingUserInfo,
    ErrorLoadingChats,
    ErrorLoadingUserInfo,
    LoadingChats,
    Chat,
    SigningOut,
}

const App = () => {
    const classes = useClasses();

    const [appState, setAppState] = React.useState(AppState.ProbeForBackend);
    const dispatch = useAppDispatch();

    const { instance, inProgress } = useMsal();
    const { features, isMaintenance } = useAppSelector((state: RootState) => state.app);
    const isAuthenticated = useIsAuthenticated();

    const chat = useChat();
    const file = useFile();
    const user = useGraph();

    useEffect(() => {
        if (isMaintenance && appState !== AppState.ProbeForBackend) {
            setAppState(AppState.ProbeForBackend);
            return;
        }

        if (isAuthenticated && appState === AppState.SettingUserInfo) {
            const account = instance.getActiveAccount();

            if (!account) {
                setAppState(AppState.ErrorLoadingUserInfo);
            } else {
                void Promise.all([
                    user
                        .userData()
                        .then((user) => {
                            dispatch(
                                setActiveUserInfo({
                                    // id: `${account.localAccountId}.${account.tenantId}`,
                                    id: account.localAccountId,
                                    email: account.username,
                                    username: account.name ?? account.username,
                                    graphUserData: user.userGraphData ?? undefined,
                                    graphUserGroups: user.userGroups ?? undefined,
                                }),
                            );
                        })
                        .catch(() => {
                            setAppState(AppState.ErrorLoadingUserInfo);
                        }),
                ]);

                // Privacy disclaimer for internal Microsoft users
                if (account.username.split('@')[1] === 'microsoft.com') {
                    dispatch(
                        addAlert({
                            message:
                                'By using Chat Copilot, you agree to protect sensitive data, not store it in chat, and allow chat history collection for service improvements. This tool is for internal use only.',
                            type: AlertType.Info,
                        }),
                    );
                }

                setAppState(AppState.LoadingChats);
            }
        }

        if ((isAuthenticated || !AuthHelper.isAuthAAD()) && appState === AppState.LoadingChats) {
            void Promise.all([
                // Load all chats from memory
                chat
                    .loadChats()
                    .then(() => {
                        setAppState(AppState.Chat);
                    })
                    .catch(() => {
                        setAppState(AppState.ErrorLoadingChats);
                    }),

                // Check if content safety is enabled
                file.getContentSafetyStatus(),

                // Load service information
                chat.getServiceInfo().then((serviceInfo) => {
                    if (serviceInfo) {
                        dispatch(setServiceInfo(serviceInfo));
                    }
                }),

                // if MsGraph is enabled, connect it
                // a promise for the below function

                (async () => {
                    const name = BuiltInPlugins.MsGraph;
                    if (initialState.plugins[name].enabled) {
                        await TokenHelper.getAccessTokenUsingMsal(
                            inProgress,
                            instance,
                            initialState.plugins[name].authRequirements.scopes ?? [],
                        )
                            .then((token) => {
                                dispatch(
                                    connectPlugin({
                                        plugin: name,
                                        accessToken: token,
                                    }),
                                );
                            })
                            .catch(() => {
                                dispatch(
                                    addAlert({
                                        message: `Failed to connect to ${name}. Check your permissions and try again.`,
                                        type: AlertType.Error,
                                    }),
                                );
                            });
                    }
                })(),
            ]);
        }

        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [instance, inProgress, isAuthenticated, appState, isMaintenance]);

    const content = <Chat classes={classes} appState={appState} setAppState={setAppState} />;
    return (
        <FluentProvider
            className="app-container"
            theme={features[FeatureKeys.DarkMode].enabled ? semanticKernelDarkTheme : semanticKernelLightTheme}
        >
            {AuthHelper.isAuthAAD() ? (
                <>
                    <UnauthenticatedTemplate>
                        <div className={classes.container}>
                            <div className={classes.header}>
                                <Subtitle1 as="h1">Chat Copilot</Subtitle1>
                            </div>
                            {appState === AppState.SigningOut && <Loading text="Signing you out..." />}
                            {appState !== AppState.SigningOut && <Login />}
                        </div>
                    </UnauthenticatedTemplate>
                    <AuthenticatedTemplate>{content}</AuthenticatedTemplate>
                </>
            ) : (
                content
            )}
        </FluentProvider>
    );
};

const Chat = ({
    classes,
    appState,
    setAppState,
}: {
    classes: ReturnType<typeof useClasses>;
    appState: AppState;
    setAppState: (state: AppState) => void;
}) => {
    const onBackendFound = React.useCallback(() => {
        setAppState(
            AuthHelper.isAuthAAD()
                ? // if AAD is enabled, we need to set the active account before loading chats
                  AppState.SettingUserInfo
                : // otherwise, we can load chats immediately
                  AppState.LoadingChats,
        );
    }, [setAppState]);
    return (
        <div className={classes.container}>
            <div className={classes.header}>
                <Subtitle1 as="h1">Chat Copilot</Subtitle1>
                {appState > AppState.SettingUserInfo && (
                    <div className={classes.cornerItems}>
                        <div className={classes.cornerItems}>
                            <PluginGallery />
                            <UserSettingsMenu
                                setLoadingState={() => {
                                    setAppState(AppState.SigningOut);
                                }}
                            />
                        </div>
                    </div>
                )}
            </div>
            {appState === AppState.ProbeForBackend && <BackendProbe onBackendFound={onBackendFound} />}
            {appState === AppState.SettingUserInfo && (
                <Loading text={'Hang tight while we fetch your information...'} />
            )}
            {appState === AppState.ErrorLoadingUserInfo && (
                <Error text={'Unable to load user info. Please try signing out and signing back in.'} />
            )}
            {appState === AppState.ErrorLoadingChats && (
                <Error text={'Unable to load chats. Please try refreshing the page.'} />
            )}
            {appState === AppState.LoadingChats && <Loading text="Loading chats..." />}
            {appState === AppState.Chat && <ChatView />}
        </div>
    );
};

export default App;
