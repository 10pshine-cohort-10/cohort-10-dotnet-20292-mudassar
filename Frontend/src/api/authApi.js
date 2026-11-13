import client from "./axiosClient";

export const loginRequest = (email, password) => client.post("/auth/login", { email, password }).then((r) => r.data);
export const registerRequest = (dto) => client.post("/auth/register", dto).then((r) => r.data);
export const getMe = () => client.get("/auth/me").then((r) => r.data);
export const logoutRequest = () => client.post("/auth/logout");
