import { NavLink, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useSettings } from '../settings/SettingsContext';

export function MorePage() {
  const { user, logout } = useAuth();
  const settings = useSettings();
  const navigate = useNavigate();

  const onLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="page-pad">
      <h1 className="page-title">More</h1>
      <div className="card more-user-card">
        <strong>{user?.username}</strong>
        <div className="muted small">
          {settings.storeName} · Admin
        </div>
      </div>
      <div className="menu-list">
        <NavLink className="menu-list-item touch-target" to="/products">
          <span>Products &amp; barcodes</span>
          <span className="muted" aria-hidden="true">
            ›
          </span>
        </NavLink>
        <button type="button" className="menu-list-item touch-target" style={{ width: '100%', border: 'none', cursor: 'pointer', font: 'inherit' }} onClick={onLogout}>
          <span style={{ color: 'var(--kaif-danger)' }}>Sign out</span>
          <span className="muted" aria-hidden="true">
            ⎋
          </span>
        </button>
      </div>
    </div>
  );
}
