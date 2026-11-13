export function getErrorMessage(error) {
  if (!error.response) return "Cannot reach the server. Please try again.";
  const data = error.response.data;
  if (typeof data === "string" && data.trim()) return data;
  if (data?.errors) return Object.values(data.errors).flat().join(" ");
  if (data?.title) return data.title;
  if (data?.detail) return data.detail;
  return "Something went wrong.";
}
