import { Navigate, Outlet, Route, Routes } from "react-router-dom";
import { useAuth } from "./auth/AuthContext";
import ProtectedRoute from "./auth/ProtectedRoute";
import AppLayout from "./components/AppLayout";
import Login from "./pages/Login";
import Signup from "./pages/Signup";
import Dashboard from "./pages/Dashboard";

function PublicOnly() {
  const { user, loading } = useAuth();
  if (loading) return null;
  return user ? <Navigate to="/dashboard" replace /> : <Outlet />;
}

// Placeholder until each screen is built.
const Placeholder = ({ name }) => <h1>{name} (coming soon)</h1>;

export default function App() {
  return (
    <Routes>
      <Route element={<PublicOnly />}>
        <Route path="/login" element={<Login />} />
        <Route path="/signup" element={<Signup />} />
      </Route>

      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route path="/dashboard" element={<Dashboard />} />
          <Route path="/tasks" element={<Placeholder name="Task list" />} />
          <Route path="/tasks/new" element={<Placeholder name="New task" />} />
          <Route path="/tasks/:id" element={<Placeholder name="Task detail" />} />
          <Route path="/tasks/:id/edit" element={<Placeholder name="Edit task" />} />
          <Route path="/profile" element={<Placeholder name="Profile" />} />
        </Route>
      </Route>

      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
