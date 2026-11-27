import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { deleteTask, getTasks, updateTaskStatus } from "../api/tasksApi";
import { getCategories } from "../api/categoriesApi";
import { getErrorMessage } from "../utils/errorMessage";
import { formatDueDate } from "../utils/formatDate";
import { deletePageCacheByPrefix, getPageCache, setPageCache } from "../utils/pageCache";
import PageHeader from "../components/PageHeader";
import ErrorAlert from "../components/ErrorAlert";
import StatusBadge from "../components/StatusBadge";
import PriorityBadge from "../components/PriorityBadge";

const PAGE_SIZE = 10;

const fmtDate = (d) => formatDueDate(d);

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

function buildTaskQueryParams({ page, statusFilter, priorityFilter, categoryFilter, searchQuery }) {
  const params = { page, pageSize: PAGE_SIZE };
  if (statusFilter) params.status = statusFilter;
  if (priorityFilter) params.priority = priorityFilter;
  if (categoryFilter) params.categoryId = Number(categoryFilter);
  if (searchQuery.trim()) params.search = searchQuery.trim();
  return params;
}

const isCanceledError = (e) => e?.code === "ERR_CANCELED" || e?.name === "CanceledError";
const toArray = (value) => (Array.isArray(value) ? value : []);
const computeTotalPages = (res) =>
  res.totalPages || Math.ceil((res.totalCount || 0) / PAGE_SIZE);

const settleLookup = (promise) =>
  promise.then(
    (value) => ({ ok: true, value }),
    (reason) => ({ ok: false, reason })
  );

function resolveCategoriesFromOutcome(catsOutcome, fallbackCategories, setCategories, setCategoryError) {
  if (catsOutcome.ok) {
    const nextCats = toArray(catsOutcome.value);
    setCategories(nextCats);
    setCategoryError("");
    return nextCats;
  }
  setCategoryError(getErrorMessage(catsOutcome.reason));
  return toArray(fallbackCategories);
}

const isStaleLoad = ({ isMountedRef, signal, requestGenRef, gen, currentOwnerRef, loadOwner }) =>
  !isMountedRef.current ||
  signal.aborted ||
  requestGenRef.current !== gen ||
  currentOwnerRef.current !== loadOwner;

const invalidateOwnerCaches = (owner) => {
  deletePageCacheByPrefix(`tasks:${owner}:`);
  deletePageCacheByPrefix(`dashboard:${owner}`);
};

function CategoryErrorAlert({ categoryError, retryingCategories, onRetry }) {
  if (!categoryError) return null;
  return (
    <div className="alert error" role="alert">
      <span>{categoryError}</span>
      <div className="row-actions">
        <button
          type="button"
          className="retry"
          disabled={retryingCategories}
          onClick={onRetry}
        >
          {retryingCategories ? "Retrying..." : "Retry categories"}
        </button>
      </div>
    </div>
  );
}

function TaskRow({
  task,
  allowDelete,
  statusPending,
  deletePending,
  onStatusChange,
  onDelete,
}) {
  return (
    <tr>
      <td>
        <Link className="task-title-link" to={`/tasks/${task.id}`}>{task.title}</Link>
      </td>
      <td>{task.categoryName || <span className="muted">Uncategorized</span>}</td>
      <td>{task.assignedToName || <span className="muted">Unassigned</span>}</td>
      <td><PriorityBadge priority={task.priority} /></td>
      <td>
        <div className="status-cell">
          <StatusBadge status={task.status} />
          <select
            className="status-quick-select"
            aria-label={`Change status for ${task.title}`}
            value={task.status}
            disabled={statusPending}
            onChange={(e) => onStatusChange(task, e.target.value)}
          >
            <option value="Pending">Pending</option>
            <option value="InProgress">In Progress</option>
            <option value="Completed">Completed</option>
          </select>
        </div>
      </td>
      <td className={isOverdue(task.dueDate, task.status) ? "due overdue" : "due"}>
        {fmtDate(task.dueDate)}
      </td>
      <td>
        <div className="row-actions">
          <Link to={`/tasks/${task.id}`} className="btn-ghost btn-sm">View</Link>
          <Link to={`/tasks/${task.id}/edit`} className="btn-ghost btn-sm">Edit</Link>
          {allowDelete && (
            <button
              type="button"
              className="btn-ghost btn-sm btn-danger"
              disabled={deletePending}
              onClick={() => onDelete(task)}
            >
              Delete
            </button>
          )}
        </div>
      </td>
    </tr>
  );
}

function TaskPagination({ page, totalPages, onPageChange }) {
  if (totalPages <= 1) return null;
  return (
    <nav className="pagination" aria-label="Task pagination">
      <button
        type="button"
        className="btn-ghost btn-sm"
        disabled={page <= 1}
        onClick={() => onPageChange(page - 1)}
      >
        Previous
      </button>
      <span className="muted">Page {page} of {totalPages}</span>
      <button
        type="button"
        className="btn-ghost btn-sm"
        disabled={page >= totalPages}
        onClick={() => onPageChange(page + 1)}
      >
        Next
      </button>
    </nav>
  );
}

export default function TaskList() {
  const { user, isAdmin } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const { statusFilter, priorityFilter, categoryFilter, searchQuery, page } =
    parseTaskFilters(searchParams);

  const ownerKey = user?.id ?? "anon";
  const userCachePrefix = `tasks:${ownerKey}:`;
  const cacheKey = `${userCachePrefix}${page}:${statusFilter}:${priorityFilter}:${categoryFilter}:${searchQuery}`;
  const cached = getPageCache(cacheKey);

  const [data, setData] = useState(() => cached?.data ?? EMPTY_DATA);
  const [dataKey, setDataKey] = useState(() => (cached ? cacheKey : null));
  const [categories, setCategories] = useState(() => cached?.categories ?? []);
  const [loading, setLoading] = useState(() => !cached);
  const [pendingStatusIds, setPendingStatusIds] = useState(() => new Set());
  const [pendingDeleteIds, setPendingDeleteIds] = useState(() => new Set());
  const pendingDeleteIdsRef = useRef(new Set());
  const [error, setError] = useState("");
  const [categoryError, setCategoryError] = useState("");
  const [retryingCategories, setRetryingCategories] = useState(false);

  const isMountedRef = useRef(true);
  const controllerRef = useRef(null);
  const requestGenRef = useRef(0);
  const categoryGenRef = useRef(0);
  const searchParamsRef = useRef(searchParams);
  searchParamsRef.current = searchParams;
  const cacheKeyRef = useRef(cacheKey);
  cacheKeyRef.current = cacheKey;
  const currentOwnerRef = useRef(ownerKey);
  currentOwnerRef.current = ownerKey;
  const categoriesRef = useRef(categories);
  categoriesRef.current = categories;

  const load = useCallback(async () => {
    if (!isMountedRef.current) return;
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const { signal } = controller;
    const gen = ++requestGenRef.current;
    const loadOwner = currentOwnerRef.current;

    const params = buildTaskQueryParams({
      page,
      statusFilter,
      priorityFilter,
      categoryFilter,
      searchQuery,
    });

    const cachedEntry = getPageCache(cacheKey);
    if (cachedEntry) {
      setData(cachedEntry.data ?? EMPTY_DATA);
      setDataKey(cacheKey);
      setCategories(toArray(cachedEntry.categories));
      setCategoryError("");
      setLoading(false);
    } else {
      setLoading(true);
    }

    setError("");
    try {
      const [tasksRes, catsOutcome] = await Promise.all([
        getTasks(params, { signal }),
        settleLookup(getCategories()),
      ]);
      const staleCtx = { isMountedRef, signal, requestGenRef, gen, currentOwnerRef, loadOwner };
      if (isStaleLoad(staleCtx)) return;
      const nextData = tasksRes ?? EMPTY_DATA;
      const computedTotalPages = computeTotalPages(nextData);
      if (computedTotalPages > 0 && page > computedTotalPages) {
        const clamped = new URLSearchParams(searchParamsRef.current);
        clamped.set("page", String(computedTotalPages));
        setSearchParams(clamped, { replace: true });
        return;
      }
      const nextCats = resolveCategoriesFromOutcome(
        catsOutcome,
        categoriesRef.current,
        setCategories,
        setCategoryError
      );
      setData(nextData);
      setDataKey(cacheKey);
      setLoading(false);
      setPageCache(cacheKey, { data: nextData, categories: nextCats });
    } catch (e) {
      const staleCtx = { isMountedRef, signal, requestGenRef, gen, currentOwnerRef, loadOwner };
      if (isStaleLoad(staleCtx) || isCanceledError(e)) {
        return;
      }
      setError(getErrorMessage(e));
      setLoading(false);
    }
  }, [page, statusFilter, priorityFilter, categoryFilter, searchQuery, cacheKey, setSearchParams]);

  const loadRef = useRef(load);
  loadRef.current = load;

  const retryCategories = useCallback(async () => {
    if (!isMountedRef.current) return;
    const gen = ++categoryGenRef.current;
    const retryOwner = currentOwnerRef.current;
    setRetryingCategories(true);
    setCategoryError("");
    try {
      const catsRes = await getCategories();
      if (
        !isMountedRef.current ||
        categoryGenRef.current !== gen ||
        currentOwnerRef.current !== retryOwner
      ) {
        return;
      }
      const nextCats = toArray(catsRes);
      setCategories(nextCats);
      const currentEntry = getPageCache(cacheKeyRef.current);
      if (currentEntry) {
        setPageCache(cacheKeyRef.current, { ...currentEntry, categories: nextCats });
      }
    } catch (e) {
      if (
        !isMountedRef.current ||
        categoryGenRef.current !== gen ||
        currentOwnerRef.current !== retryOwner
      ) {
        return;
      }
      setCategoryError(getErrorMessage(e));
    } finally {
      if (isMountedRef.current && categoryGenRef.current === gen) {
        setRetryingCategories(false);
      }
    }
  }, []);

  useEffect(() => {
    isMountedRef.current = true;
    const controller = controllerRef;
    const reqGen = requestGenRef;
    const catGen = categoryGenRef;
    return () => {
      isMountedRef.current = false;
      controller.current?.abort();
      reqGen.current += 1;
      catGen.current += 1;
    };
  }, []);

  useEffect(() => {
    void load();
    return () => controllerRef.current?.abort();
  }, [load]);

  const updateFilter = (key, val) => {
    const next = new URLSearchParams(searchParams);
    if (val) next.set(key, val);
    else next.delete(key);
    next.set("page", "1");
    setSearchParams(next, { replace: key === "search" });
  };

  const goToPage = (nextPage) => {
    const next = new URLSearchParams(searchParams);
    next.set("page", String(nextPage));
    setSearchParams(next);
  };

  const canReloadForOwner = (owner) =>
    isMountedRef.current && currentOwnerRef.current === owner;

  const handleStatusChange = async (task, newStatus) => {
    if (pendingStatusIds.has(task.id)) return;
    const originatingCacheKey = cacheKeyRef.current;
    const originatingOwner = currentOwnerRef.current;
    setError("");
    setPendingStatusIds((prev) => new Set(prev).add(task.id));
    try {
      const updated = await updateTaskStatus(task.id, newStatus);
      invalidateOwnerCaches(originatingOwner);
      if (!canReloadForOwner(originatingOwner)) return;
      if (cacheKeyRef.current === originatingCacheKey) {
        setData((prev) => {
          const nextStatus = updated?.status ?? newStatus;
          const matchesStatusFilter = !statusFilter || nextStatus === statusFilter;
          const prevItems = prev.items ?? [];
          const nextItems = matchesStatusFilter
            ? prevItems.map((item) => (item.id === task.id ? updated : item))
            : prevItems.filter((item) => item.id !== task.id);
          return {
            ...prev,
            items: nextItems,
          };
        });
      }
      // Invalidate any in-flight read and reload via the latest load ref.
      requestGenRef.current += 1;
      await loadRef.current();
    } catch (e) {
      if (isMountedRef.current) setError(getErrorMessage(e));
    } finally {
      if (isMountedRef.current) {
        setPendingStatusIds((prev) => {
          const next = new Set(prev);
          next.delete(task.id);
          return next;
        });
      }
    }
  };

  const handleDelete = async (task) => {
    if (pendingDeleteIdsRef.current.has(task.id) || pendingDeleteIds.has(task.id)) return;
    if (!window.confirm(`Delete task "${task.title}"?`)) return;
    const originatingCacheKey = cacheKeyRef.current;
    const originatingOwner = currentOwnerRef.current;
    pendingDeleteIdsRef.current.add(task.id);
    setPendingDeleteIds((prev) => new Set(prev).add(task.id));
    setError("");
    try {
      await deleteTask(task.id);
      invalidateOwnerCaches(originatingOwner);
      if (!canReloadForOwner(originatingOwner)) return;
      if (cacheKeyRef.current === originatingCacheKey) {
        setData((prev) => ({
          ...prev,
          items: (prev.items ?? []).filter((item) => item.id !== task.id),
          totalCount: Math.max(0, (prev.totalCount || 0) - 1),
        }));
      }
      requestGenRef.current += 1;
      await loadRef.current();
    } catch (e) {
      if (isMountedRef.current) setError(getErrorMessage(e));
    } finally {
      pendingDeleteIdsRef.current.delete(task.id);
      if (isMountedRef.current) {
        setPendingDeleteIds((prev) => {
          const next = new Set(prev);
          next.delete(task.id);
          return next;
        });
      }
    }
  };

  const isCurrentData = dataKey === cacheKey;
  const items = isCurrentData ? data.items ?? [] : [];
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
      <CategoryErrorAlert
        categoryError={categoryError}
        retryingCategories={retryingCategories}
        onRetry={() => void retryCategories()}
      />

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

      {loading && !isCurrentData && !error && (
        <div className="form-card" aria-busy="true" aria-label="Loading tasks">
          <div className="skel" style={{ height: 24, width: "35%", marginBottom: 14 }} />
          <div className="skel" style={{ height: 42, marginBottom: 10 }} />
          <div className="skel" style={{ height: 42 }} />
        </div>
      )}

      {isCurrentData && items.length === 0 && (
        <div className="empty">
          <h2>No tasks found</h2>
          <p>Try clearing your filters or create a new task to get started.</p>
          <Link to="/tasks/new" className="btn-primary">+ Create Task</Link>
        </div>
      )}

      {isCurrentData && items.length > 0 && (
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
                <TaskRow
                  key={t.id}
                  task={t}
                  allowDelete={canDelete(t)}
                  statusPending={pendingStatusIds.has(t.id)}
                  deletePending={pendingDeleteIds.has(t.id)}
                  onStatusChange={handleStatusChange}
                  onDelete={handleDelete}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}

      {isCurrentData && (
        <TaskPagination page={page} totalPages={totalPages} onPageChange={goToPage} />
      )}
    </>
  );
}
