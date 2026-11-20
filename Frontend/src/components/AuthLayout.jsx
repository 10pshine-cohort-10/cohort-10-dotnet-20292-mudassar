import hero from "../assets/auth-hero.svg";
import ThemeToggle from "./ThemeToggle";

const Logo = () => (
  <div className="logo">
    <span className="logo-mark" aria-hidden="true">
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M3 8.5l3.2 3L13 4.5" stroke="#fff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" /></svg>
    </span>
    TaskFlow
  </div>
);

export default function AuthLayout({ title, subtitle, children }) {
  return (
    <div className="auth">
      <aside className="brand" aria-hidden="true">
        <Logo />
        <div>
          <h2>Know what to do next, and get it done.</h2>
          <p className="lead">Plan tasks, set priorities and track progress with your team in one calm workspace.</p>
          <img src={hero} alt="" className="hero" />
        </div>
        <small>© {new Date().getFullYear()} TaskFlow</small>
      </aside>
      <main className="panel">
        <ThemeToggle />
        <div className="card">
          <div className="mobile-logo"><Logo /></div>
          <h1>{title}</h1>
          <p className="sub">{subtitle}</p>
          {children}
        </div>
      </main>
    </div>
  );
}
