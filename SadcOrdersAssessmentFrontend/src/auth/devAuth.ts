import { setTokenProvider } from '../api/client';
import { api } from '../api/endpoints';

/**
 * Development auth for the API's mock mode: gets a locally signed JWT for the selected roles from
 * POST /api/dev/token. With real Microsoft Entra ID, swap this for MSAL
 * (PublicClientApplication.acquireTokenSilent) behind the same setTokenProvider hook.
 */
export interface RoleProfile {
  label: 'Reader' | 'Writer' | 'Admin';
  roles: string[];
  description: string;
}

export const ROLE_PROFILES: RoleProfile[] = [
  { label: 'Reader', roles: ['Orders.Read'], description: 'View customers and orders' },
  { label: 'Writer', roles: ['Orders.Read', 'Orders.Write'], description: 'Create and update' },
  { label: 'Admin', roles: ['Orders.Read', 'Orders.Write', 'Orders.Admin'], description: 'Can also cancel paid orders' },
];

export const DEFAULT_PROFILE: RoleProfile = ROLE_PROFILES[1]!;

let currentRoles = DEFAULT_PROFILE.roles;
let cached: { roles: string[]; token: string; expiresAt: number } | null = null;
let inFlight: Promise<string> | null = null;

const EXPIRY_MARGIN_MS = 60_000;

export function setActiveRoles(roles: string[]): void {
  currentRoles = roles;
  cached = null;
  inFlight = null;
}

async function getToken(): Promise<string> {
  const roles = currentRoles;
  if (cached && cached.roles === roles && cached.expiresAt - EXPIRY_MARGIN_MS > Date.now()) {
    return cached.token;
  }
  inFlight ??= api
    .issueDevToken(roles)
    .then((result) => {
      if (roles === currentRoles) {
        cached = { roles, token: result.accessToken, expiresAt: Date.parse(result.expiresAt) };
      }
      return result.accessToken;
    })
    .finally(() => {
      inFlight = null;
    });
  return inFlight;
}

export function initDevAuth(): void {
  setTokenProvider(getToken);
}
