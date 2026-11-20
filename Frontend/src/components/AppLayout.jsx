import { Outlet } from "react-router-dom";
import Navbar from "./Navbar";
import "../styles/dashboard.css";

export default function AppLayout() {
  return (
    <div className="app">
      <a className="skip" href="#main">Skip to content</a>
      <Navbar />
      <main id="main" className="page">
        <Outlet />
      </main>
    </div>
  );
}
