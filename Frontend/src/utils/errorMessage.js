export function getErrorMessage(error) {
  if (!error.response) return "Cannot reach the server. Please try again.";
  const data = error.response.data;
  if (data?.errors) return Object.values(data.errors).flat().join(" ");
  if (data?.title) return data.title;
  return "Something went wrong.";
}
