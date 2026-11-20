import { createContext, useContext } from 'react';
import type { RoleProfile } from './devAuth';

export interface AuthContextValue {
  profile: RoleProfile;
  setProfile: (profile: RoleProfile) => void;
  can: (role: string) => boolean;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error('useAuth must be used inside <AuthProvider>');
  }
  return value;
}
