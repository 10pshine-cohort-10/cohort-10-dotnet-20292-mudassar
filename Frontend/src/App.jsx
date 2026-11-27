import { Navigate, Outlet, Route, Routes } from "react-router-dom";
import { useAuth } from "./auth/AuthContext";
import ProtectedRoute, { SessionRecoveryView } from "./auth/ProtectedRoute";
import AppLayout from "./components/AppLayout";
import Login from "./pages/Login";
import Signup from "./pages/Signup";
import Dashboard from "./pages/Dashboard";
import TaskList from "./pages/TaskList";
import TaskForm from "./pages/TaskForm";
import TaskDetail from "./pages/TaskDetail";
import Profile from "./pages/Profile";

function PublicOnly() {
  const { user, loading, restoreError, retryRestore, logout } = useAuth();
  if (loading) {
    return (
      <p role="status" aria-live="polite" style={{ padding: 24 }}>
        Loading...
      </p>
    );
  }
  if (!user && restoreError) {
    return (
      <SessionRecoveryView
        message={restoreError}
        onRetry={retryRestore}
        onSignInAgain={logout}
      />
    );
  }
  return user ? <Navigate to="/dashboard" replace /> : <Outlet />;
}

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
          <Route path="/tasks" element={<TaskList />} />
          <Route path="/tasks/new" element={<TaskForm key="create" />} />
          <Route path="/tasks/:id" element={<TaskDetail />} />
          <Route path="/tasks/:id/edit" element={<TaskForm key="edit" />} />
          <Route path="/profile" element={<Profile />} />
        </Route>
      </Route>

      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
