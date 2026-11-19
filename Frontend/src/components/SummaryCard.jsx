import { Link } from "react-router-dom";

const P = { fill: "none", stroke: "currentColor", strokeWidth: 2, strokeLinecap: "round", strokeLinejoin: "round" };
const ICONS = {
  done: <svg width="18" height="18" viewBox="0 0 24 24" {...P}><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>,
  doing: <svg width="18" height="18" viewBox="0 0 24 24" {...P}><circle cx="12" cy="12" r="8" /><path d="M12 8v4l2.5 2" /></svg>,
  pending: <svg width="18" height="18" viewBox="0 0 24 24" {...P}><circle cx="12" cy="12" r="8" strokeDasharray="3 3" /></svg>,
  total: <svg width="18" height="18" viewBox="0 0 24 24" {...P}><path d="M5 7h14M5 12h14M5 17h9" /></svg>,
};

export default function SummaryCard({ to, label, value, tone, hint, loading }) {
  const displayValue = value ?? "—";
  const ariaLabel =
    loading || value == null ? label : `${label}: ${value} tasks`;
  return (
    <Link to={to} className={`scard t-${tone}`} aria-label={ariaLabel}>
      <div className="scard-top"><span className="ico" aria-hidden="true">{ICONS[tone]}</span>{label}</div>
      {loading ? (
        <div className="skel" style={{ height: 38, width: 70, marginTop: 14 }} />
      ) : (
        <div className="count">{displayValue}</div>
      )}
      <small>{hint}</small>
    </Link>
  );
}
