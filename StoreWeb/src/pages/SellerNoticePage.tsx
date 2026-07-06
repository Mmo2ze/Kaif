import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export function SellerNoticePage() {
  const { logout } = useAuth();
  const navigate = useNavigate();

  const onLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="page-pad">
      <h1 className="page-title">Use the POS app</h1>
      <p className="muted">
        Mobile web is for products and stock. Selling and sales history are available in the desktop POS app.
      </p>
      <button type="button" className="btn primary touch-target" onClick={onLogout}>
        Sign out
      </button>
    </div>
  );
}
