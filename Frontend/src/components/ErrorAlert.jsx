export default function ErrorAlert({ message, onClose, type = "error" }) {
  if (!message) return null;
  return (
    <div className={`alert ${type}`} role={type === "error" ? "alert" : "status"}>
      <p>{message}</p>
      {onClose && (
        <button type="button" onClick={onClose} aria-label="Dismiss message">×</button>
      )}
    </div>
  );
}
