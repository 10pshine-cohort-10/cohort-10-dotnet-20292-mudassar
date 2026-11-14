import { useCallback, useEffect, useMemo, useRef, useState } from "react";
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

const fmtDate = (d) =>
  d
    ? new Date(d).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })
    : "No due date";

const isOverdue = (d, status) =>
  Boolean(d && status !== "Completed" && new Date(d) < new Date(new Date().toDateString()));

const EMPTY_DATA = { items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0 };

export default function TaskList() {
  const { user, isAdmin } = useAuth();
  const cacheKey = `tasks:${user?.id ?? "anon"}`;
  const cached = getPageCache(cacheKey);

  const [searchParams, setSearchParams] = useSearchParams();

  const [data, setData] = useState(() => cached?.data ?? EMPTY_DATA);
  const [categories, setCategories] = useState(() => cached?.categories ?? []);
  const [hasLoaded, setHasLoaded] = useState(() => Boolean(cached));
  const [error, setError] = useState("");

  const statusFilter = searchParams.get("status") || "";
  const priorityFilter = searchParams.get("priority") || "";
  const categoryFilter = searchParams.get("categoryId") || "";
  const searchQuery = searchParams.get("search") || "";
  const page = Number(searchParams.get("page") || 1);

  const controllerRef = useRef(null);

  const load = useCallback(async () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const { signal } = controller;

    setError("");
    try {
      const [tasksRes, catsRes] = await Promise.all([
        getTasks({ page, pageSize: 50 }, { signal }),
        getCategories().catch(() => []),
      ]);
      if (signal.aborted) return;
      setData(tasksRes);
      setCategories(catsRes);
      setHasLoaded(true);
      setPageCache(cacheKey, { data: tasksRes, categories: catsRes });
    } catch (e) {
      if (signal.aborted || e?.code === "ERR_CANCELED" || e?.name === "CanceledError") return;
      setError(getErrorMessage(e));
      setHasLoaded(true);
    }
  }, [page, cacheKey]);

  useEffect(() => {
    void load();
    return () => controllerRef.current?.abort();
  }, [load]);

  const filteredItems = useMemo(() => {
    return (data.items ?? []).filter((t) => {
      if (statusFilter && t.status !== statusFilter) return false;
      if (priorityFilter && t.priority !== priorityFilter) return false;
      if (categoryFilter && String(t.categoryId ?? "") !== categoryFilter) return false;
      if (searchQuery) {
        const q = searchQuery.toLowerCase();
        const inTitle = t.title?.toLowerCase().includes(q);
        const inDesc = t.description?.toLowerCase().includes(q);
        const inAssignee = t.assignedToName?.toLowerCase().includes(q);
        if (!inTitle && !inDesc && !inAssignee) return false;
      }
      return true;
    });
  }, [data.items, statusFilter, priorityFilter, categoryFilter, searchQuery]);

  const updateFilter = (key, val) => {
    const next = new URLSearchParams(searchParams);
    if (val) next.set(key, val);
    else next.delete(key);
    next.set("page", "1");
    setSearchParams(next);
  };

  const handleStatusChange = async (task, newStatus) => {
    setError("");
    try {
      const updated = await updateTaskStatus(task.id, newStatus);
      setData((prev) => {
        const next = {
          ...prev,
          items: prev.items.map((item) => (item.id === task.id ? updated : item)),
        };
        setPageCache(cacheKey, { data: next, categories });
        return next;
      });
    } catch (e) {
      setError(getErrorMessage(e));
    }
  };

  const handleDelete = async (task) => {
    if (!window.confirm(`Delete task "${task.title}"?`)) return;
    setError("");
    try {
      await deleteTask(task.id);
      setData((prev) => {
        const next = {
          ...prev,
          items: prev.items.filter((item) => item.id !== task.id),
          totalCount: Math.max(0, prev.totalCount - 1),
        };
        setPageCache(cacheKey, { data: next, categories });
        return next;
      });
    } catch (e) {
      setError(getErrorMessage(e));
    }
  };

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

      {hasLoaded && filteredItems.length === 0 && (
        <div className="empty">
          <h2>No tasks found</h2>
          <p>Try clearing your filters or create a new task to get started.</p>
          <Link to="/tasks/new" className="btn-primary">+ Create Task</Link>
        </div>
      )}

      {filteredItems.length > 0 && (
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
              {filteredItems.map((t) => (
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
      )}
    </>
  );
}
