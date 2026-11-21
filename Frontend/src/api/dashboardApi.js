import client from "./axiosClient";

// Fetches Completed, InProgress, Pending, and Total task counts (scoped to user or all for Admin).
export const getSummary = (config) => client.get("/dashboard/summary", config).then((r) => r.data);
