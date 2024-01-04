/* eslint-disable no-var */
import { PublicClientApplication } from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import ReactDOM from 'react-dom/client';
import { Provider as ReduxProvider } from 'react-redux';
import App from './App';
import { Constants } from './Constants';
import './index.css';
import { AuthConfig, AuthHelper } from './libs/auth/AuthHelper';
import { store } from './redux/app/store';

import React from 'react';
import { BackendServiceUrl } from './libs/services/BaseService';
import { setAuthConfig } from './redux/features/app/appSlice';

import { ClickAnalyticsPlugin } from '@microsoft/applicationinsights-clickanalytics-js';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';
import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { createBrowserHistory } from 'history';
// eslint-disable-next-line @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-call
const browserHistory = createBrowserHistory();
const reactPlugin = new ReactPlugin();
// *** Add the Click Analytics plug-in. ***
var clickPluginInstance = new ClickAnalyticsPlugin();
var clickPluginConfig = {
    autoCapture: true,
};
var appInsights = new ApplicationInsights({
    config: {
        connectionString:
            'InstrumentationKey=19d691f4-6278-4a96-b3fc-249b4d212872;IngestionEndpoint=https://eastus2-3.in.applicationinsights.azure.com/;LiveEndpoint=https://eastus2.livediagnostics.monitor.azure.com/',
        enableAutoRouteTracking: true,
        extensions: [reactPlugin, clickPluginInstance],
        extensionConfig: {
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
            [reactPlugin.identifier]: { history: browserHistory },
            [clickPluginInstance.identifier]: clickPluginConfig,
        },
    },
});
appInsights.loadAppInsights();

if (!localStorage.getItem('debug')) {
    localStorage.setItem('debug', `${Constants.debug.root}:*`);
}

let container: HTMLElement | null = null;
let root: ReactDOM.Root | undefined = undefined;
let msalInstance: PublicClientApplication | undefined;

// const appInsights = new ApplicationInsights({
//     config: {
//         connectionString:
//             'InstrumentationKey=19d691f4-6278-4a96-b3fc-249b4d212872;IngestionEndpoint=https://eastus2-3.in.applicationinsights.azure.com/;LiveEndpoint=https://eastus2.livediagnostics.monitor.azure.com/',
//         /* ...Other Configuration Options... */
//     },
// });
// appInsights.loadAppInsights();
// appInsights.trackPageView();

document.addEventListener('DOMContentLoaded', () => {
    if (!container) {
        container = document.getElementById('root');
        if (!container) {
            throw new Error('Could not find root element');
        }
        root = ReactDOM.createRoot(container);

        renderApp();
    }
});

export function renderApp() {
    fetch(new URL('authConfig', BackendServiceUrl))
        .then((response) => (response.ok ? (response.json() as Promise<AuthConfig>) : Promise.reject()))
        .then((authConfig) => {
            store.dispatch(setAuthConfig(authConfig));

            if (AuthHelper.isAuthAAD()) {
                if (!msalInstance) {
                    msalInstance = new PublicClientApplication(AuthHelper.getMsalConfig(authConfig));
                    void msalInstance.handleRedirectPromise().then((response) => {
                        if (response) {
                            msalInstance?.setActiveAccount(response.account);
                        }
                    });
                }

                const activeAccount = msalInstance.getActiveAccount();
                if (activeAccount) {
                    appInsights.setAuthenticatedUserContext(activeAccount.localAccountId, activeAccount.username);
                }

                // render with the MsalProvider if AAD is enabled
                // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
                root!.render(
                    <React.StrictMode>
                        <ReduxProvider store={store}>
                            <MsalProvider instance={msalInstance}>
                                <App />
                            </MsalProvider>
                        </ReduxProvider>
                        ,
                    </React.StrictMode>,
                );
            }
        })
        .catch(() => {
            store.dispatch(setAuthConfig(undefined));
        });

    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    root!.render(
        <React.StrictMode>
            <ReduxProvider store={store}>
                <App />
            </ReduxProvider>
        </React.StrictMode>,
    );
}
