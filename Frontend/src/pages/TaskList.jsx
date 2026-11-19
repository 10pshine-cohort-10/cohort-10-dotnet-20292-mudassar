import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { deleteTask, getTasks, updateTaskStatus } from "../api/tasksApi";
import { getCategories } from "../api/categoriesApi";
import { getErrorMessage } from "../utils/errorMessage";
import { getPageCache, setPageCache } from "../utils/pageCache";
import PageHeader from "../components/PageHeader";
import ErrorAlert from "../components/ErrorAlert";
import StatusBadge from "../components/StatusBadge";
import PriorityBadge from "../components/PriorityBadge";

const PAGE_SIZE = 10;

const fmtDate = (d) =>
  d
    ? new Date(d).toLocaleDateString(undefined, {
        day: "numeric",
        month: "short",
        year: "numeric",
        timeZone: "UTC",
      })
    : "No due date";

const localTodayStr = () => {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, "0");
  const d = String(now.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
};

const isOverdue = (d, status) =>
  Boolean(d && status !== "Completed" && String(d).slice(0, 10) < localTodayStr());

const EMPTY_DATA = { items: [], totalCount: 0, page: 1, pageSize: PAGE_SIZE, totalPages: 0 };

function parseTaskFilters(searchParams) {
  return {
    statusFilter: searchParams.get("status") || "",
    priorityFilter: searchParams.get("priority") || "",
    categoryFilter: searchParams.get("categoryId") || "",
    searchQuery: searchParams.get("search") || "",
    page: Math.max(1, Number(searchParams.get("page") || 1) || 1),
  };
}

export default function TaskList() {
  const { user, isAdmin } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const { statusFilter, priorityFilter, categoryFilter, searchQuery, page } =
    parseTaskFilters(searchParams);

  const cacheKey = `tasks:${user?.id ?? "anon"}:${page}:${statusFilter}:${priorityFilter}:${categoryFilter}:${searchQuery}`;
  const cached = getPageCache(cacheKey);

  const [data, setData] = useState(() => cached?.data ?? EMPTY_DATA);
  const [categories, setCategories] = useState(() => cached?.categories ?? []);
  const [hasLoaded, setHasLoaded] = useState(() => Boolean(cached));
  const [pendingStatusIds, setPendingStatusIds] = useState(() => new Set());
  const [error, setError] = useState("");

  const controllerRef = useRef(null);
  const requestGenRef = useRef(0);

  const load = useCallback(async () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const { signal } = controller;
    const gen = ++requestGenRef.current;

    const params = { page, pageSize: PAGE_SIZE };
    if (statusFilter) params.status = statusFilter;
    if (priorityFilter) params.priority = priorityFilter;
    if (categoryFilter) params.categoryId = Number(categoryFilter);
    if (searchQuery.trim()) params.search = searchQuery.trim();

    setError("");
    try {
      const [tasksRes, catsRes] = await Promise.all([
        getTasks(params, { signal }),
        getCategories().catch(() => []),
      ]);
      if (signal.aborted || requestGenRef.current !== gen) return;
      setData(tasksRes);
      setCategories(catsRes);
      setHasLoaded(true);
      setPageCache(cacheKey, { data: tasksRes, categories: catsRes });
    } catch (e) {
      if (signal.aborted || requestGenRef.current !== gen || e?.code === "ERR_CANCELED" || e?.name === "CanceledError") {
        return;
      }
      setError(getErrorMessage(e));
      setHasLoaded(true);
    }
  }, [page, statusFilter, priorityFilter, categoryFilter, searchQuery, cacheKey]);

  useEffect(() => {
    void load();
    return () => controllerRef.current?.abort();
  }, [load]);

  const updateFilter = (key, val) => {
    const next = new URLSearchParams(searchParams);
    if (val) next.set(key, val);
    else next.delete(key);
    next.set("page", "1");
    setSearchParams(next);
  };

  const goToPage = (nextPage) => {
    const next = new URLSearchParams(searchParams);
    next.set("page", String(nextPage));
    setSearchParams(next);
  };

  const handleStatusChange = async (task, newStatus) => {
    if (pendingStatusIds.has(task.id)) return;
    setError("");
    setPendingStatusIds((prev) => new Set(prev).add(task.id));
    try {
      await updateTaskStatus(task.id, newStatus);
      // Invalidate any in-flight read and reload so stale responses cannot overwrite state.
      requestGenRef.current += 1;
      await load();
    } catch (e) {
      setError(getErrorMessage(e));
    } finally {
      setPendingStatusIds((prev) => {
        const next = new Set(prev);
        next.delete(task.id);
        return next;
      });
    }
  };

  const handleDelete = async (task) => {
    if (!window.confirm(`Delete task "${task.title}"?`)) return;
    setError("");
    try {
      await deleteTask(task.id);
      requestGenRef.current += 1;
      await load();
    } catch (e) {
      setError(getErrorMessage(e));
    }
  };

  const items = data.items ?? [];
  const totalPages = Math.max(1, data.totalPages || Math.ceil((data.totalCount || 0) / PAGE_SIZE));
  const canDelete = (task) => isAdmin || task.createdById === user?.id;

  return (
    <>
      <PageHeader
        title="Tasks"
        subtitle={isAdmin ? "Manage all tasks across the workspace" : "Create, track, and update your tasks"}
        actions={<Link to="/tasks/new" className="btn-primary">+ New Task</Link>}
      />

      <ErrorAlert message={error} onClose={() => setError("")} />

      <div className="filters-bar" role="search" aria-label="Filter tasks">
        <input
          type="search"
          className="filter-input"
          placeholder="Search by title, description, or assignee..."
          value={searchQuery}
          onChange={(e) => updateFilter("search", e.target.value)}
        />
        <select
          className="filter-select"
          aria-label="Filter by status"
          value={statusFilter}
          onChange={(e) => updateFilter("status", e.target.value)}
        >
          <option value="">All statuses</option>
          <option value="Pending">Pending</option>
          <option value="InProgress">In Progress</option>
          <option value="Completed">Completed</option>
        </select>
        <select
          className="filter-select"
          aria-label="Filter by priority"
          value={priorityFilter}
          onChange={(e) => updateFilter("priority", e.target.value)}
        >
          <option value="">All priorities</option>
          <option value="Low">Low</option>
          <option value="Medium">Medium</option>
          <option value="High">High</option>
          <option value="Critical">Critical</option>
        </select>
        <select
          className="filter-select"
          aria-label="Filter by category"
          value={categoryFilter}
          onChange={(e) => updateFilter("categoryId", e.target.value)}
        >
          <option value="">All categories</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
      </div>

      {hasLoaded && items.length === 0 && (
        <div className="empty">
          <h2>No tasks found</h2>
          <p>Try clearing your filters or create a new task to get started.</p>
          <Link to="/tasks/new" className="btn-primary">+ Create Task</Link>
        </div>
      )}

      {items.length > 0 && (
        <>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Category</th>
                  <th>Assignee</th>
                  <th>Priority</th>
                  <th>Status</th>
                  <th>Due date</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((t) => (
                  <tr key={t.id}>
                    <td>
                      <Link className="task-title-link" to={`/tasks/${t.id}`}>{t.title}</Link>
                    </td>
                    <td>{t.categoryName || <span className="muted">Uncategorized</span>}</td>
                    <td>{t.assignedToName || <span className="muted">Unassigned</span>}</td>
                    <td><PriorityBadge priority={t.priority} /></td>
                    <td>
                      <div className="status-cell">
                        <StatusBadge status={t.status} />
                        <select
                          className="status-quick-select"
                          aria-label={`Change status for ${t.title}`}
                          value={t.status}
                          disabled={pendingStatusIds.has(t.id)}
                          onChange={(e) => handleStatusChange(t, e.target.value)}
                        >
                          <option value="Pending">Pending</option>
                          <option value="InProgress">In Progress</option>
                          <option value="Completed">Completed</option>
                        </select>
                      </div>
                    </td>
                    <td className={isOverdue(t.dueDate, t.status) ? "due overdue" : "due"}>
                      {fmtDate(t.dueDate)}
                    </td>
                    <td>
                      <div className="row-actions">
                        <Link to={`/tasks/${t.id}`} className="btn-ghost btn-sm">View</Link>
                        <Link to={`/tasks/${t.id}/edit`} className="btn-ghost btn-sm">Edit</Link>
                        {canDelete(t) && (
                          <button
                            type="button"
                            className="btn-ghost btn-sm btn-danger"
                            onClick={() => handleDelete(t)}
                          >
                            Delete
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {totalPages > 1 && (
            <nav className="pagination" aria-label="Task pagination">
              <button
                type="button"
                className="btn-ghost btn-sm"
                disabled={page <= 1}
                onClick={() => goToPage(page - 1)}
              >
                Previous
              </button>
              <span className="muted">Page {page} of {totalPages}</span>
              <button
                type="button"
                className="btn-ghost btn-sm"
                disabled={page >= totalPages}
                onClick={() => goToPage(page + 1)}
              >
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </>
  );
}
