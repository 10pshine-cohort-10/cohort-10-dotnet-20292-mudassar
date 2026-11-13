import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { deleteTask, getTasks, updateTaskStatus } from "../api/tasksApi";
import { getCategories } from "../api/categoriesApi";
import { getErrorMessage } from "../utils/errorMessage";
import ErrorAlert from "../components/ErrorAlert";
import StatusBadge from "../components/StatusBadge";
import PriorityBadge from "../components/PriorityBadge";

const fmtDate = (d) =>
  d
    ? new Date(d).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })
    : "No due date";

const isOverdue = (d, status) =>
  Boolean(d && status !== "Completed" && new Date(d) < new Date(new Date().toDateString()));

export default function TaskList() {
  const { user, isAdmin } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();

  const [data, setData] = useState({ items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0 });
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const statusFilter = searchParams.get("status") || "";
  const priorityFilter = searchParams.get("priority") || "";
  const categoryFilter = searchParams.get("categoryId") || "";
  const searchQuery = searchParams.get("search") || "";
  const page = Number(searchParams.get("page") || 1);

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const [tasksRes, catsRes] = await Promise.all([
        getTasks({ page, pageSize: 50 }),
        getCategories().catch(() => [])
      ]);
      setData(tasksRes);
      setCategories(catsRes);
    } catch (e) {
      setError(getErrorMessage(e));
    } finally {
      setLoading(false);
    }
  }, [page]);

  useEffect(() => {
    load();
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
      setData((prev) => ({
        ...prev,
        items: prev.items.map((item) => (item.id === task.id ? updated : item))
      }));
    } catch (e) {
      setError(getErrorMessage(e));
    }
  };

  const handleDelete = async (task) => {
    if (!window.confirm(`Delete task "${task.title}"?`)) return;
    setError("");
    try {
      await deleteTask(task.id);
      setData((prev) => ({
        ...prev,
        items: prev.items.filter((item) => item.id !== task.id),
        totalCount: Math.max(0, prev.totalCount - 1)
      }));
    } catch (e) {
      setError(getErrorMessage(e));
    }
  };

  const canDelete = (task) => isAdmin || task.createdById === user?.id;

  return (
    <>
      <div className="head">
        <div>
          <h1>Tasks</h1>
          <p>{isAdmin ? "Manage all tasks across the workspace" : "Create, track, and update your tasks"}</p>
        </div>
        <Link to="/tasks/new" className="btn-primary">+ New Task</Link>
      </div>

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

      {!loading && filteredItems.length === 0 ? (
        <div className="empty">
          <h2>No tasks found</h2>
          <p>Try clearing your filters or create a new task to get started.</p>
          <Link to="/tasks/new" className="btn-primary">+ Create Task</Link>
        </div>
      ) : (
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
              {loading
                ? Array.from({ length: 5 }, (_, i) => (
                    <tr key={i}>
                      <td colSpan={7}><div className="skel" style={{ height: 18 }} /></td>
                    </tr>
                  ))
                : filteredItems.map((t) => (
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

