import { NavLink } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

const navClass = ({ isActive }: { isActive: boolean }) => `bottom-nav-item${isActive ? ' active' : ''}`;

export function BottomNav() {
  const { isAdmin } = useAuth();

  return (
    <nav className="bottom-nav" aria-label="Main">
      {isAdmin && (
        <NavLink className={navClass} to="/home">
          <span aria-hidden="true">⌂</span>
          <span>Home</span>
        </NavLink>
      )}
      <NavLink className={navClass} to="/products">
        <span aria-hidden="true">▦</span>
        <span>Products</span>
      </NavLink>
      {isAdmin && (
        <NavLink className={navClass} to="/more">
          <span aria-hidden="true">⋯</span>
          <span>More</span>
        </NavLink>
      )}
    </nav>
  );
}
