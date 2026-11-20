# Task Management Tool – Frontend (React)

React single-page app for the Task Management Tool. It talks to the ASP.NET Core API (see `../backend/README.md`) using JWT authentication.

## Tech Stack

- React 18 + Vite
- React Router 6 (routing, protected routes)
- Axios (HTTP client with interceptors)
- React Hook Form + Zod (forms and validation)
- Context API (auth state) – swap for Redux Toolkit if the app grows
- Vitest + React Testing Library, ESLint + Prettier
- SonarQube (JavaScript/TypeScript analysis)
- Optional: `@microsoft/signalr` for real-time updates

## Quick Start

```bash
cd Frontend
npm install
cp env.example .env
npm run dev          # http://localhost:5173
```

`.env`
```
VITE_API_BASE_URL=https://localhost:5001/api
```

> Make sure the backend CORS policy allows `http://localhost:5173`.

## Scripts

| Command | Purpose |
|---|---|
| `npm run dev` | Dev server |
| `npm run build` | Production build |
| `npm run preview` | Preview production build |
| `npm run lint` | ESLint static analysis |
| `npm run sonar` | SonarQube scan (requires `sonar-scanner`) |

## Project Structure

```
src/
├── api/            axiosClient.js, authApi.js, tasksApi.js, dashboardApi.js, usersApi.js
├── auth/           AuthContext.jsx, ProtectedRoute.jsx, RoleRoute.jsx
├── components/     Navbar, StatusBadge, PriorityBadge, ConfirmDialog, Pagination, ErrorAlert
├── pages/
│   ├── Login.jsx / Signup.jsx
│   ├── Dashboard.jsx
│   ├── TaskList.jsx
│   ├── TaskDetail.jsx
│   ├── TaskForm.jsx          (create + edit)
│   └── Profile.jsx
├── hooks/          useTasks.js, useDebounce.js
├── utils/          errorMessage.js, constants.js
├── App.jsx         route table
└── main.jsx
```

## Screens and Routes

| Route | Screen | Access | What it does |
|---|---|---|---|
| `/login` | Login | Public | `POST /auth/login`, store token, redirect to `/dashboard` |
| `/signup` | Sign Up | Public | `POST /auth/register`, then redirect to login |
| `/dashboard` | Dashboard | Auth | Completed / In Progress / Pending counts via `GET /dashboard/summary` (backend returns all-task counts for admins) |
| `/tasks` | Task List | Auth | Filters (status, priority, category, search), pagination, link to detail, **New Task** button |
| `/tasks/new` | New Task | Auth | Form → `POST /tasks` |
| `/tasks/:id` | Task Detail | Auth | `GET /tasks/{id}`, edit and delete actions |
| `/tasks/:id/edit` | Edit Task | Auth | Form prefilled → `PUT /tasks/{id}` |
| `/profile` | User Profile | Auth | `GET /auth/me`, **Logout** button |

Admins additionally see an assignee dropdown (`GET /users`) with all users and a global task view.

## Key Implementation Snippets

### Axios client with token + error handling

```js
// src/api/axiosClient.js
import axios from "axios";
import { getActiveSessionStorage } from "../auth/tokenStorage";

const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || "/api",
  timeout: 15000,
});
const PUBLIC_PATHS = ["/auth/login", "/auth/register"];

client.interceptors.request.use(async (config) => {
  const token = await getActiveSessionStorage().getToken();
  config._requestToken = token;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

client.interceptors.response.use(
  (res) => res,
  async (error) => {
    const url = error.config?.url ?? "";
    const isPublic = PUBLIC_PATHS.some((p) => url.includes(p));
    const requestToken = error.config?._requestToken;
    const storage = getActiveSessionStorage();
    if (error.response?.status === 401 && !isPublic && requestToken) {
      const removed = await storage.removeTokenIfMatch(requestToken);
      if (removed) {
        window.location.href = "/login";
      }
    }
    return Promise.reject(error);
  }
);

export default client;
```

### Turning API errors (ProblemDetails) into messages

```js
// src/utils/errorMessage.js
export function getErrorMessage(error) {
  const data = error.response?.data;
  if (data?.errors) return Object.values(data.errors).flat().join(" ");   // validation errors
  if (data?.title) return data.title;                                     // ProblemDetails
  if (!error.response) return "Cannot reach the server. Please try again.";
  return "Something went wrong.";
}
```

### Auth context and route guards

```jsx
// src/auth/AuthContext.jsx
import { createContext, useContext, useEffect, useState } from "react";
import client from "../api/axiosClient";
import { defaultSessionStorage, setActiveSessionStorage } from "./tokenStorage";

const AuthContext = createContext(null);
export const useAuth = () => useContext(AuthContext);

export function AuthProvider({ children, storage = defaultSessionStorage }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setActiveSessionStorage(storage);
    let active = true;
    (async () => {
      try {
        const token = await storage.getToken();
        if (!token) return;
        const { data } = await client.get("/auth/me");
        if (active) setUser(data);
      } catch {
        await storage.removeToken().catch(() => {});
        if (active) setUser(null);
      } finally {
        if (active) setLoading(false);
      }
    })();
    return () => {
      active = false;
    };
  }, [storage]);

  const login = async (email, password) => {
    setActiveSessionStorage(storage);
    const { data } = await client.post("/auth/login", { email, password });
    await storage.setToken(data.token);
    setUser(data.user);
  };

  const logout = async () => {
    setActiveSessionStorage(storage);
    try {
      await client.post("/auth/logout");
    } finally {
      await storage.removeToken();
      setUser(null);
    }
  };

  return (
    <AuthContext.Provider
      value={{ user, loading, login, logout, isAdmin: !!user?.roles?.includes("Admin") }}
    >
      {children}
    </AuthContext.Provider>
  );
}
```

```jsx
// src/auth/ProtectedRoute.jsx
import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";

export default function ProtectedRoute({ adminOnly = false }) {
  const { user, loading, isAdmin } = useAuth();
  if (loading) return <p>Loading...</p>;
  if (!user) return <Navigate to="/login" replace />;
  if (adminOnly && !isAdmin) return <Navigate to="/dashboard" replace />;
  return <Outlet />;
}
```

### Routes

```jsx
// src/App.jsx
<Routes>
  <Route path="/login" element={<Login />} />
  <Route path="/signup" element={<Signup />} />
  <Route element={<ProtectedRoute />}>
    <Route path="/dashboard" element={<Dashboard />} />
    <Route path="/tasks" element={<TaskList />} />
    <Route path="/tasks/new" element={<TaskForm />} />
    <Route path="/tasks/:id" element={<TaskDetail />} />
    <Route path="/tasks/:id/edit" element={<TaskForm />} />
    <Route path="/profile" element={<Profile />} />
  </Route>
  <Route path="*" element={<Navigate to="/dashboard" replace />} />
</Routes>
```

### Task API module

```js
// src/api/tasksApi.js
import client from "./axiosClient";

export const getTasks   = (params) => client.get("/tasks", { params }).then((r) => r.data);
export const getTask    = (id)     => client.get(`/tasks/${id}`).then((r) => r.data);
export const createTask = (dto)    => client.post("/tasks", dto).then((r) => r.data);
export const updateTask = (id, dto)=> client.put(`/tasks/${id}`, dto).then((r) => r.data);
export const deleteTask = (id)     => client.delete(`/tasks/${id}`);
```

### Dashboard cards

```jsx
const [summary, setSummary] = useState(null);
useEffect(() => { client.get("/dashboard/summary").then((r) => setSummary(r.data)); }, []);

<div className="cards">
  <Card title="Completed"   value={summary?.completed} />
  <Card title="In Progress" value={summary?.inProgress} />
  <Card title="Pending"     value={summary?.pending} />
</div>
```

### Task form validation

```js
const schema = z.object({
  title: z.string().min(1, "Title is required").max(200),
  description: z.string().max(2000).optional(),
  priority: z.enum(["Low", "Medium", "High", "Critical"]),
  status: z.enum(["Pending", "InProgress", "Completed"]),
  dueDate: z.string().optional(),
  categoryId: z.coerce.number().optional(),
  assignedToId: z.string().optional(),
});
```

Use `useForm({ resolver: zodResolver(schema) })`; the same form component handles create (no `:id`) and edit (prefill with `getTask(id)`).

### Task list filters

Keep filters in the URL (`useSearchParams`) so lists are shareable/bookmarkable, and debounce the search input (300 ms) before calling `getTasks({ status, priority, categoryId, search, page, pageSize })`.

## UX Requirements

- Show loading and empty states on every data screen.
- Show API errors via an `ErrorAlert` using `getErrorMessage`.
- Confirm before delete; disable buttons while a request is in flight.
- Responsive layout (CSS grid/flex or Tailwind), accessible labels and focus states.

## Token Storage Note

The application persists session tokens through the asynchronous IndexedDB-backed store in `src/auth/tokenStorage.js` (`defaultSessionStorage`), with `BroadcastChannel` cross-tab synchronization and an in-memory fallback when IndexedDB is unavailable. `AuthProvider` and `axiosClient` share the active store via `setActiveSessionStorage` / `getActiveSessionStorage`. For high-security deployments, pair short-lived access tokens with an `httpOnly` cookie refresh-token flow, and always sanitize rendered user content (React escapes by default; avoid `dangerouslySetInnerHTML`).

## Optional: Real-time (SignalR)

```js
import * as signalR from "@microsoft/signalr";
import { getActiveSessionStorage } from "./auth/tokenStorage";
const conn = new signalR.HubConnectionBuilder()
  .withUrl("https://localhost:5001/hubs/tasks", {
    accessTokenFactory: () => getActiveSessionStorage().getToken(),
  })
  .withAutomaticReconnect().build();
conn.on("TaskUpdated", () => refetch());
conn.start();
```

## SonarQube

Uses [sonar-project.properties](./sonar-project.properties):

```bash
npm run lint
npm run sonar -- -Dsonar.token=$SONAR_TOKEN
```

## Git Workflow

- Branches: `main`, `develop`, `feature/<name>`, `bugfix/<name>`.
- Small PRs, CI runs lint + tests + Sonar before merge.
- Conventional commits (`feat:`, `fix:`, `chore:`). Don't commit `.env` or `node_modules`.

Copy `env.example` to `.env` before running (`cp env.example .env`).
