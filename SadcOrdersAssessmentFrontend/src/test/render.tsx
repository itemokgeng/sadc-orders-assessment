import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { SWRConfig } from 'swr';
import { AuthProvider } from '../auth/AuthProvider';
import { ROLE_PROFILES, type RoleProfile } from '../auth/devAuth';

interface Options {
  route?: string;
  /** Route pattern for the component under test; defaults to the route's pathname. */
  path?: string;
  profile?: RoleProfile['label'];
}

/** Renders with a fresh SWR cache (no leaks between tests), a router and the auth context. */
export function renderWithProviders(ui: ReactElement, { route = '/', path, profile = 'Writer' }: Options = {}) {
  return render(
    <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0, shouldRetryOnError: false }}>
      <MemoryRouter initialEntries={[route]}>
        <AuthProvider initialProfile={ROLE_PROFILES.find((p) => p.label === profile)}>
          <Routes>
            <Route path={path ?? route.split('?')[0]} element={ui} />
            <Route path="/orders/:id" element={<p>Order details route</p>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>
    </SWRConfig>,
  );
}
