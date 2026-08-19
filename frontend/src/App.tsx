// App root: BrowserRouter + lazy-loaded routes inside the Layout shell.
// Code-splitting: each page is a separate chunk loaded on navigation.

import { lazy, Suspense } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { ErrorBoundary } from './components/ErrorBoundary';
import { Layout } from './components/Layout';
import { ToastProvider } from './components/Toast';
import { UserProvider } from './context/UserProvider';
import { Spinner } from './components/ui';
import Placeholder from './pages/Placeholder';

const Dashboard = lazy(() => import('./pages/Dashboard'));
const Holdings = lazy(() => import('./pages/Holdings'));
const Trades = lazy(() => import('./pages/Trades'));
const SettingsPage = lazy(() => import('./pages/Settings'));
const Signals = lazy(() => import('./pages/Signals'));
const SystemPortfolio = lazy(() => import('./pages/SystemPortfolio'));
const Compare = lazy(() => import('./pages/Compare'));
const SignalPerformance = lazy(() => import('./pages/SignalPerformance'));
const Admin = lazy(() => import('./pages/Admin'));

function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <UserProvider>
          <ErrorBoundary>
            <Suspense fallback={<Spinner />}>
              <Routes>
                <Route element={<Layout />}>
                  <Route index element={<Dashboard />} />
                  <Route path="holdings" element={<Holdings />} />
                  <Route path="trades" element={<Trades />} />
                  <Route path="signals" element={<Signals />} />
                  <Route path="system" element={<SystemPortfolio />} />
                  <Route path="compare" element={<Compare />} />
                  <Route path="performance" element={<SignalPerformance />} />
                  <Route path="admin" element={<Admin />} />
                  <Route path="settings" element={<SettingsPage />} />
                  <Route path="*" element={<Placeholder title="Not Found" />} />
                </Route>
              </Routes>
            </Suspense>
          </ErrorBoundary>
        </UserProvider>
      </ToastProvider>
    </BrowserRouter>
  );
}

export default App;
