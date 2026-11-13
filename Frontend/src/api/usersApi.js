import client from "./axiosClient";

export const getUserLookup = () => client.get("/users/lookup").then((r) => r.data);
export const getUsers = (params) => client.get("/users", { params }).then((r) => r.data);
export const changeUserRole = (id, role) => client.put(`/users/${id}/role`, { role });
export const setUserActive = (id, isActive) => client.put(`/users/${id}/active`, { isActive });

