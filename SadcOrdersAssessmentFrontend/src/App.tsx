import { NavLink, Navigate, Route, Routes } from 'react-router';
import { ROLE_PROFILES } from './auth/devAuth';
import { useAuth } from './auth/useAuth';
import { CreateOrderPage } from './pages/CreateOrderPage';
import { CustomersPage } from './pages/CustomersPage';
import { OrderDetailsPage } from './pages/OrderDetailsPage';
import { OrdersPage } from './pages/OrdersPage';

function RoleSwitcher() {
  const { profile, setProfile } = useAuth();
  return (
    <div className="field inline">
      <label htmlFor="role">Acting as</label>
      <select
        id="role"
        value={profile.label}
        onChange={(e) => setProfile(ROLE_PROFILES.find((p) => p.label === e.target.value) ?? profile)}
      >
        {ROLE_PROFILES.map((p) => (
          <option key={p.label} value={p.label} title={p.description}>
            {p.label}
          </option>
        ))}
      </select>
    </div>
  );
}

export function App() {
  return (
    <>
      <a href="#main" className="skip-link">
        Skip to content
      </a>
      <header className="app-header">
        <h1>SADC Orders</h1>
        <nav aria-label="Main">
          <NavLink to="/customers">Customers</NavLink>
          <NavLink to="/orders" end>
            Orders
          </NavLink>
          <NavLink to="/orders/new">New order</NavLink>
        </nav>
        <RoleSwitcher />
      </header>
      <main id="main">
        <Routes>
          <Route path="/" element={<Navigate to="/orders" replace />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/orders" element={<OrdersPage />} />
          <Route path="/orders/new" element={<CreateOrderPage />} />
          <Route path="/orders/:id" element={<OrderDetailsPage />} />
          <Route path="*" element={<p>Page not found.</p>} />
        </Routes>
      </main>
    </>
  );
}
