const LABELS = { Pending: "Pending", InProgress: "In Progress", Completed: "Completed" };
const ICONS = { Pending: "○", InProgress: "◐", Completed: "●" }; // shape + text, never color alone

export default function StatusBadge({ status }) {
  return (
    <span className={`badge b-${String(status).toLowerCase()}`}>
      <span aria-hidden="true">{ICONS[status]}</span>{LABELS[status] ?? status}
    </span>
  );
}
