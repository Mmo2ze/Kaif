import { NavLink } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useSettings } from '../settings/SettingsContext';

export function DesktopNav() {
  const { isAdmin } = useAuth();
  const settings = useSettings();

  const linkClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'active' : undefined);

  return (
    <aside className="app-side-desktop" aria-label="Main">
      <div className="app-side-brand">{settings.storeName}</div>
      {isAdmin && (
        <NavLink to="/home" className={linkClass}>
          Home
        </NavLink>
      )}
      <NavLink to="/products" className={linkClass}>
        Products
      </NavLink>
      {isAdmin && (
        <NavLink to="/more" className={linkClass}>
          More
        </NavLink>
      )}
    </aside>
  );
}
