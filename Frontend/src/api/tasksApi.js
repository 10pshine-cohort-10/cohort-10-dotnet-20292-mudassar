import client from "./axiosClient";

export const getTasks = (params, config) => client.get("/tasks", { params, ...config }).then((r) => r.data);
export const getTaskById = (id) => client.get(`/tasks/${id}`).then((r) => r.data);
export const createTask = (dto) => client.post("/tasks", dto).then((r) => r.data);
export const updateTask = (id, dto) => client.put(`/tasks/${id}`, dto).then((r) => r.data);
export const updateTaskStatus = (id, status) =>
  client.patch(`/tasks/${id}/status`, { status }).then((r) => r.data);
export const deleteTask = (id) => client.delete(`/tasks/${id}`);

export const exportTasks = (format, params) =>
  client.get("/tasks/export", {
    params: { ...params, format },
    responseType: "blob",
  });

export const importTasks = (file) => {
  const formData = new FormData();
  formData.append("file", file);
  return client.post("/tasks/import", formData).then((r) => r.data);
};
