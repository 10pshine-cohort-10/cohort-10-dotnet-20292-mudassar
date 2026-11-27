export default function LiveBadge({ state }) {
  if (state !== "connected") return null;
  return (
    <span className="live-pill" title="Real-time SignalR task status updates active">
      <span className="live-dot" aria-hidden="true" />
      Live
    </span>
  );
}

