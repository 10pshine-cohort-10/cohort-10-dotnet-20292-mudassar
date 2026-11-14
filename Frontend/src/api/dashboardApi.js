import client from "./axiosClient";

// `config` lets callers pass { signal } to cancel the request on unmount.
export const getSummary = (config) => client.get("/dashboard/summary", config).then((r) => r.data);
