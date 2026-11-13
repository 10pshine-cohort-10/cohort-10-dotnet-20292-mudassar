import client from "./axiosClient";

export const getSummary = () => client.get("/dashboard/summary").then((r) => r.data);
