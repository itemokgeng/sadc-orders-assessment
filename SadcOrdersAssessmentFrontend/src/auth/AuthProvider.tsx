import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useSWRConfig } from 'swr';
import { DEFAULT_PROFILE, setActiveRoles, type RoleProfile } from './devAuth';
import { AuthContext } from './useAuth';

export function AuthProvider({ children, initialProfile = DEFAULT_PROFILE }: { children: ReactNode; initialProfile?: RoleProfile }) {
  const [profile, setProfileState] = useState(initialProfile);
  const { mutate } = useSWRConfig();

  const setProfile = useCallback(
    (next: RoleProfile) => {
      setActiveRoles(next.roles);
      setProfileState(next);
      // What a user may see can depend on their role, so refetch everything.
      void mutate(() => true);
    },
    [mutate],
  );

  const value = useMemo(
    () => ({ profile, setProfile, can: (role: string) => profile.roles.includes(role) }),
    [profile, setProfile],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
