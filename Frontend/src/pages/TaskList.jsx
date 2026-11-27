import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { deleteTask, getTasks, updateTaskStatus } from "../api/tasksApi";
import { getCategories } from "../api/categoriesApi";
import { getUserLookup } from "../api/usersApi";
import { getErrorMessage } from "../utils/errorMessage";
import { formatDueDate } from "../utils/formatDate";
import { deletePageCacheByPrefix, getPageCache, setPageCache } from "../utils/pageCache";
import { useTaskStatusRealtime } from "../realtime/taskHubClient";
import PageHeader from "../components/PageHeader";
import LiveBadge from "../components/LiveBadge";
import TaskTransferControls, { ImportResultAlert } from "../components/TaskTransferControls";
import TaskFiltersBar from "../components/TaskFiltersBar";
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
    assigneeFilter: searchParams.get("assignedToId") || "",
    dueFrom: searchParams.get("dueFrom") || "",
    dueTo: searchParams.get("dueTo") || "",
    overdueOnly: searchParams.get("overdueOnly") === "true",
    sortBy: searchParams.get("sortBy") || "createdAt",
    sortDir: searchParams.get("sortDir") || "desc",
    searchQuery: searchParams.get("search") || "",
    page: Math.max(1, Number(searchParams.get("page") || 1) || 1),
  };
}

function buildFilterQueryFields(filters) {
  const params = {};
  if (filters.statusFilter) params.status = filters.statusFilter;
  if (filters.priorityFilter) params.priority = filters.priorityFilter;
  if (filters.categoryFilter) params.categoryId = Number(filters.categoryFilter);
  if (filters.assigneeFilter) params.assignedToId = filters.assigneeFilter;
  if (filters.dueFrom) params.dueFrom = filters.dueFrom;
  if (filters.dueTo) params.dueTo = filters.dueTo;
  if (filters.overdueOnly) params.overdueOnly = true;
  if (filters.sortBy && filters.sortBy !== "createdAt") params.sortBy = filters.sortBy;
  if (filters.sortDir && filters.sortDir !== "desc") params.sortDir = filters.sortDir;
  if (filters.searchQuery.trim()) params.search = filters.searchQuery.trim();
  return params;
}

function buildTaskQueryParams(filters) {
  return { page: filters.page, pageSize: PAGE_SIZE, ...buildFilterQueryFields(filters) };
}

function buildTaskCacheKey(userCachePrefix, filters) {
  return [
    userCachePrefix,
    filters.page,
    filters.statusFilter,
    filters.priorityFilter,
    filters.categoryFilter,
    filters.assigneeFilter,
    filters.dueFrom,
    filters.dueTo,
    filters.overdueOnly ? "1" : "0",
    filters.sortBy,
    filters.sortDir,
    filters.searchQuery,
  ].join(":");
}

const isCanceledError = (e) => e?.code === "ERR_CANCELED" || e?.name === "CanceledError";
const toArray = (value) => (Array.isArray(value) ? value : []);
const computeTotalPages = (res) =>
  res.totalPages || Math.ceil((res.totalCount || 0) / PAGE_SIZE);

const isStaleLoad = ({ isMountedRef, signal, requestGenRef, gen, currentOwnerRef, loadOwner }) =>
  !isMountedRef.current ||
  signal.aborted ||
  requestGenRef.current !== gen ||
  currentOwnerRef.current !== loadOwner;

const invalidateOwnerCaches = (owner) => {
  deletePageCacheByPrefix(`tasks:${owner}:`);
  deletePageCacheByPrefix(`dashboard:${owner}`);
};

function shouldReloadForRealtimeStatus(items, event, statusFilter) {
  const hasTask = items.some((item) => item.id === event.taskId);
  if (hasTask) return true;
  if (!statusFilter) return false;
  return event.oldStatus === statusFilter || event.newStatus === statusFilter;
}

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
  const filters = parseTaskFilters(searchParams);
  const { statusFilter, page } = filters;

  const ownerKey = user?.id ?? "anon";
  const userCachePrefix = `tasks:${ownerKey}:`;
  const cacheKey = buildTaskCacheKey(userCachePrefix, filters);
  const cached = getPageCache(cacheKey);

  const [data, setData] = useState(() => cached?.data ?? EMPTY_DATA);
  const [dataKey, setDataKey] = useState(() => (cached ? cacheKey : null));
  const [categories, setCategories] = useState(() => cached?.categories ?? []);
  const [assignees, setAssignees] = useState([]);
  const [loading, setLoading] = useState(() => !cached);
  const [pendingStatusIds, setPendingStatusIds] = useState(() => new Set());
  const [pendingDeleteIds, setPendingDeleteIds] = useState(() => new Set());
  const pendingDeleteIdsRef = useRef(new Set());
  const [error, setError] = useState("");
  const [categoryError, setCategoryError] = useState("");
  const [retryingCategories, setRetryingCategories] = useState(false);
  const [importResult, setImportResult] = useState(null);

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
  const categoriesLoadedRef = useRef(false);
  const dataRef = useRef(data);
  dataRef.current = data;

  const load = useCallback(async ({ silent = false } = {}) => {
    if (!isMountedRef.current) return;
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const { signal } = controller;
    const gen = ++requestGenRef.current;
    const loadOwner = currentOwnerRef.current;

    const params = buildTaskQueryParams(parseTaskFilters(searchParamsRef.current));

    const cachedEntry = silent ? null : getPageCache(cacheKey);
    if (cachedEntry) {
      setData(cachedEntry.data ?? EMPTY_DATA);
      setDataKey(cacheKey);
      if (!categoriesLoadedRef.current && cachedEntry.categories?.length) {
        const cachedCats = toArray(cachedEntry.categories);
        categoriesRef.current = cachedCats;
        setCategories(cachedCats);
      }
      setLoading(false);
    } else if (!silent) {
      setLoading(true);
    }

    setError("");
    try {
      const tasksRes = await getTasks(params, { signal });
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
      setData(nextData);
      setDataKey(cacheKey);
      setLoading(false);
      setPageCache(cacheKey, { data: nextData, categories: categoriesRef.current });
    } catch (e) {
      const staleCtx = { isMountedRef, signal, requestGenRef, gen, currentOwnerRef, loadOwner };
      if (isStaleLoad(staleCtx) || isCanceledError(e)) {
        return;
      }
      setError(getErrorMessage(e));
      setLoading(false);
    }
  }, [page, cacheKey, setSearchParams]);

  const loadRef = useRef(load);
  loadRef.current = load;

  const fetchCategories = useCallback(async ({ isRetry = false } = {}) => {
    if (!isMountedRef.current) return;
    const gen = ++categoryGenRef.current;
    const fetchOwner = currentOwnerRef.current;
    setRetryingCategories(isRetry);
    setCategoryError("");
    try {
      const catsRes = await getCategories();
      if (
        !isMountedRef.current ||
        categoryGenRef.current !== gen ||
        currentOwnerRef.current !== fetchOwner
      ) {
        return;
      }
      const nextCats = toArray(catsRes);
      categoriesLoadedRef.current = true;
      categoriesRef.current = nextCats;
      setCategories(nextCats);
      const currentEntry = getPageCache(cacheKeyRef.current);
      if (currentEntry) {
        setPageCache(cacheKeyRef.current, { ...currentEntry, categories: nextCats });
      }
    } catch (e) {
      if (
        !isMountedRef.current ||
        categoryGenRef.current !== gen ||
        currentOwnerRef.current !== fetchOwner
      ) {
        return;
      }
      setCategoryError(getErrorMessage(e));
    } finally {
      if (isRetry && isMountedRef.current && categoryGenRef.current === gen) {
        setRetryingCategories(false);
      }
    }
  }, []);

  const retryCategories = useCallback(
    () => fetchCategories({ isRetry: true }),
    [fetchCategories]
  );

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
    categoriesLoadedRef.current = false;
    void fetchCategories();
  }, [ownerKey, fetchCategories]);

  useEffect(() => {
    let active = true;
    getUserLookup()
      .then((res) => {
        if (active && isMountedRef.current) setAssignees(toArray(res));
      })
      .catch(() => {
        // Assignee filter remains optional if lookup fails.
      });
    return () => {
      active = false;
    };
  }, [ownerKey]);

  useEffect(() => {
    void load();
    return () => controllerRef.current?.abort();
  }, [load]);

  const updateFilter = (key, val) => {
    const next = new URLSearchParams(searchParamsRef.current);
    if (val) next.set(key, val);
    else next.delete(key);
    next.set("page", "1");
    setSearchParams(next, { replace: key === "search" });
  };

  const updateSort = (nextSortBy, nextSortDir) => {
    const next = new URLSearchParams(searchParamsRef.current);
    if (nextSortBy && nextSortBy !== "createdAt") next.set("sortBy", nextSortBy);
    else next.delete("sortBy");
    if (nextSortDir && nextSortDir !== "desc") next.set("sortDir", nextSortDir);
    else next.delete("sortDir");
    next.set("page", "1");
    setSearchParams(next);
  };

  const clearFilters = () => {
    setSearchParams(new URLSearchParams(), { replace: true });
  };

  const goToPage = (nextPage) => {
    const next = new URLSearchParams(searchParams);
    next.set("page", String(nextPage));
    setSearchParams(next);
  };

  const canReloadForOwner = (owner) =>
    isMountedRef.current && currentOwnerRef.current === owner;

  const handleRealtimeStatusUpdate = useCallback(
    (event) => {
      if (!isMountedRef.current || !event?.taskId) return;
      invalidateOwnerCaches(currentOwnerRef.current);

      const currentItems = dataRef.current?.items ?? [];
      if (shouldReloadForRealtimeStatus(currentItems, event, statusFilter)) {
        requestGenRef.current += 1;
        void loadRef.current({ silent: true });
      }
    },
    [statusFilter]
  );

  const handleRealtimeReconnected = useCallback(() => {
    if (!isMountedRef.current) return;
    invalidateOwnerCaches(currentOwnerRef.current);
    requestGenRef.current += 1;
    void loadRef.current({ silent: true });
  }, []);

  const realtimeState = useTaskStatusRealtime(
    user?.id,
    handleRealtimeStatusUpdate,
    handleRealtimeReconnected
  );

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

  const handleTasksImported = useCallback((result) => {
    setImportResult(result);
    if (result?.importedCount > 0) {
      invalidateOwnerCaches(currentOwnerRef.current);
      requestGenRef.current += 1;
      void loadRef.current({ silent: true });
    }
  }, []);

  const exportFilterParams = buildFilterQueryFields(filters);

  return (
    <>
      <PageHeader
        title="Tasks"
        subtitle={isAdmin ? "Manage all tasks across the workspace" : "Create, track, and update your tasks"}
        actions={
          <div className="row-actions">
            <LiveBadge state={realtimeState} />
            <TaskTransferControls
              filterParams={exportFilterParams}
              onImported={handleTasksImported}
              onError={setError}
            />
            <Link to="/tasks/new" className="btn-primary">+ New Task</Link>
          </div>
        }
      />

      <ErrorAlert message={error} onClose={() => setError("")} />
      <ImportResultAlert result={importResult} onClose={() => setImportResult(null)} />
      <CategoryErrorAlert
        categoryError={categoryError}
        retryingCategories={retryingCategories}
        onRetry={() => void retryCategories()}
      />

      <TaskFiltersBar
        filters={filters}
        categories={categories}
        assignees={assignees}
        onUpdateFilter={updateFilter}
        onUpdateSort={updateSort}
        onClearFilters={clearFilters}
      />

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
