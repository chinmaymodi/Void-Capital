// App root: BrowserRouter + lazy-loaded routes inside the Layout shell.
// Code-splitting: each page is a separate chunk loaded on navigation.

import { lazy, Suspense } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { ToastProvider } from './components/Toast';
import { Spinner } from './components/ui';
import Placeholder from './pages/Placeholder';

const Dashboard = lazy(() => import('./pages/Dashboard'));
const Holdings = lazy(() => import('./pages/Holdings'));
const Trades = lazy(() => import('./pages/Trades'));
const SettingsPage = lazy(() => import('./pages/Settings'));

function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <Suspense fallback={<Spinner />}>
          <Routes>
            <Route element={<Layout />}>
              <Route index element={<Dashboard />} />
              <Route path="holdings" element={<Holdings />} />
              <Route path="trades" element={<Trades />} />
              <Route path="signals" element={<Placeholder title="Signals" />} />
              <Route path="system" element={<Placeholder title="System Portfolio" />} />
              <Route path="compare" element={<Placeholder title="Compare" />} />
              <Route path="performance" element={<Placeholder title="Performance" />} />
              <Route path="admin" element={<Placeholder title="Admin" />} />
              <Route path="settings" element={<SettingsPage />} />
              <Route path="*" element={<Placeholder title="Not Found" />} />
            </Route>
          </Routes>
        </Suspense>
      </ToastProvider>
    </BrowserRouter>
  );
}

export default App;
