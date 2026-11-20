import client from "./axiosClient";

export const getCategories = () => client.get("/categories").then((r) => r.data);
export const createCategory = (dto) => client.post("/categories", dto).then((r) => r.data);
export const updateCategory = (id, dto) => client.put(`/categories/${id}`, dto).then((r) => r.data);
export const deleteCategory = (id) => client.delete(`/categories/${id}`);

