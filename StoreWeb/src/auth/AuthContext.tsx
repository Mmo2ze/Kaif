import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import * as api from '../api';
import type { CurrentUserDto, LoginRequest } from '../types';

interface AuthContextValue {
  user: CurrentUserDto | null;
  isAdmin: boolean;
  sessionReady: boolean;
  isAuthenticated: boolean;
  login: (body: LoginRequest) => Promise<CurrentUserDto>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUserDto | null>(null);
  const [sessionReady, setSessionReady] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const token = api.getStoredToken();
      const stored = api.getStoredUser();
      if (!token || !stored) {
        if (!cancelled) setSessionReady(true);
        return;
      }
      try {
        const me = await api.getMe();
        if (!cancelled) {
          setUser(me);
          api.saveSession(token, me);
        }
      } catch {
        api.clearSession();
        if (!cancelled) setUser(null);
      } finally {
        if (!cancelled) setSessionReady(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const login = useCallback(async (body: LoginRequest) => {
    const res = await api.login(body);
    api.saveSession(res.token, { id: 0, username: body.username, role: 'seller' });
    const me = await api.getMe();
    api.saveSession(res.token, me);
    setUser(me);
    return me;
  }, []);

  const logout = useCallback(() => {
    api.clearSession();
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAdmin: user?.role === 'admin',
      sessionReady,
      isAuthenticated: user != null,
      login,
      logout,
    }),
    [user, sessionReady, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
