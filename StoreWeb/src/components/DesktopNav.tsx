import { NavLink } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useSettings } from '../settings/SettingsContext';

export function DesktopNav() {
  const { isAdmin } = useAuth();
  const settings = useSettings();

  const linkClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'active' : undefined);

  if (!isAdmin) {
    return null;
  }

  return (
    <aside className="app-side-desktop" aria-label="Main">
      <div className="app-side-brand">{settings.storeName}</div>
      <NavLink to="/home" className={linkClass}>
        Home
      </NavLink>
      <NavLink to="/stock" className={linkClass}>
        Stock
      </NavLink>
      <NavLink to="/products" className={linkClass}>
        Products
      </NavLink>
    </aside>
  );
}
