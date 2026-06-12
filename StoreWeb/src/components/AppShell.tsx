import { useEffect } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useSettings } from '../settings/SettingsContext';
import { BottomNav } from './BottomNav';
import { DesktopNav } from './DesktopNav';

export function AppShell() {
  const { user, isAuthenticated, logout } = useAuth();
  const settings = useSettings();
  const { load: loadSettings } = settings;
  const navigate = useNavigate();

  useEffect(() => {
    if (isAuthenticated) void loadSettings();
  }, [isAuthenticated, loadSettings]);

  const onLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="app-shell">
      <DesktopNav />
      <header className="app-header">
        <div className="app-header-brand">
          <span className="app-header-title">{settings.storeName}</span>
          <span className="app-header-user muted">{user?.username}</span>
        </div>
        <button type="button" className="btn-icon touch-target app-header-signout" title="Sign out" onClick={onLogout}>
          <span aria-hidden="true">⎋</span>
          <span>Out</span>
        </button>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
      <BottomNav />
    </div>
  );
}
