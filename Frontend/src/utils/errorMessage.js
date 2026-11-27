export function getErrorMessage(error) {
  if (!error?.response) {
    if (error?.retryable && error?.message) return error.message;
    return "Cannot reach the server. Please try again.";
  }
  const data = error.response.data;
  if (typeof data === "string" && data.trim()) return data;
  if (data?.errors && typeof data.errors === "object") {
    const joined = Object.values(data.errors).flat().filter(Boolean).join(" ").trim();
    if (joined) return joined;
  }
  if (data?.title) return data.title;
  if (data?.detail) return data.detail;
  // Empty bodies (e.g. a 404 from an unknown route) used to collapse into a generic
  // message that hid the real cause; keep the status so the failure is diagnosable.
  const status = error.response.status;
  if (status === 404) return "The requested resource was not found (404).";
  return `Something went wrong (HTTP ${status}).`;
}
