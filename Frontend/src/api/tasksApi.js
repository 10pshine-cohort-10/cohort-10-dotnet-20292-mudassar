import client from "./axiosClient";

export const getTasks = (params) => client.get("/tasks", { params }).then((r) => r.data);
