import { useState } from "react";
import { Link, NavLink, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import ThemeToggle from "./ThemeToggle";

export default function Navbar() {
  const { user, isAdmin, logout } = useAuth();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);

  const handleLogout = async () => {
    await logout();
    navigate("/login", { replace: true });
  };
  const link = ({ isActive }) => `link${isActive ? " active" : ""}`;
  const close = () => setOpen(false);

  return (
    <nav className="nav" aria-label="Main">
      <div className="nav-in">
        <Link to="/dashboard" className="brand-link" onClick={close}>
          <span className="brand-mark" aria-hidden="true">
            <svg width="15" height="15" viewBox="0 0 16 16" fill="none"><path d="M3 8.5l3.2 3L13 4.5" stroke="#fff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" /></svg>
          </span>
          TaskFlow
        </Link>
        <div id="menu" className={`menu${open ? " open" : ""}`}>
          <NavLink to="/dashboard" className={link} onClick={close}>Dashboard</NavLink>
          <NavLink to="/tasks" className={link} onClick={close}>Tasks</NavLink>
          <NavLink to="/profile" className={link} onClick={close}>Profile</NavLink>
          <div className="user">
            <span>{user?.fullName}</span>
            <span className="role">{isAdmin ? "Admin" : "User"}</span>
            <button className="btn-ghost" onClick={handleLogout}>Log out</button>
          </div>
        </div>
        <ThemeToggle />
        <button className="burger" aria-label="Toggle menu" aria-expanded={open} aria-controls="menu" onClick={() => setOpen((o) => !o)}>
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            {open ? <path d="M6 6l12 12M18 6L6 18" /> : <path d="M4 7h16M4 12h16M4 17h16" />}
          </svg>
        </button>
      </div>
    </nav>
  );
}
