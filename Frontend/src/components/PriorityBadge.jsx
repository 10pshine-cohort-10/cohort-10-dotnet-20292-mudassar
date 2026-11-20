const ICONS = { Low: "▽", Medium: "◇", High: "△", Critical: "▲" };

export default function PriorityBadge({ priority }) {
  return (
    <span className={`badge b-${String(priority).toLowerCase()}`}>
      <span aria-hidden="true">{ICONS[priority]}</span>{priority}
    </span>
  );
}
