import client from "./axiosClient";

export const loginRequest = (email, password) => client.post("/auth/login", { email, password }).then((r) => r.data);
export const registerRequest = (dto) => client.post("/auth/register", dto).then((r) => r.data);
export const getMe = (config) => client.get("/auth/me", config).then((r) => r.data);
export const updateMe = (dto) => client.put("/auth/me", dto).then((r) => r.data);
export const logoutRequest = () => client.post("/auth/logout");
