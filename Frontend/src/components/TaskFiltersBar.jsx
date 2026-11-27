import { useEffect, useRef, useState } from "react";

const SEARCH_DEBOUNCE_MS = 300;

const SORT_OPTIONS = [
  { value: "createdAt:desc", label: "Newest first" },
  { value: "createdAt:asc", label: "Oldest first" },
  { value: "dueDate:asc", label: "Due date (soonest)" },
  { value: "dueDate:desc", label: "Due date (latest)" },
  { value: "priority:desc", label: "Priority (highest)" },
  { value: "priority:asc", label: "Priority (lowest)" },
  { value: "title:asc", label: "Title (A–Z)" },
  { value: "title:desc", label: "Title (Z–A)" },
];

function countActiveFilters(filters) {
  let count = 0;
  if (filters.searchQuery.trim()) count += 1;
  if (filters.statusFilter) count += 1;
  if (filters.priorityFilter) count += 1;
  if (filters.categoryFilter) count += 1;
  if (filters.assigneeFilter) count += 1;
  if (filters.dueFrom) count += 1;
  if (filters.dueTo) count += 1;
  if (filters.overdueOnly) count += 1;
  if (filters.sortBy && (filters.sortBy !== "createdAt" || filters.sortDir === "asc")) count += 1;
  return count;
}

export default function TaskFiltersBar({
  filters,
  categories,
  assignees,
  onUpdateFilter,
  onUpdateSort,
  onClearFilters,
}) {
  const [searchInput, setSearchInput] = useState(filters.searchQuery);
  const onUpdateFilterRef = useRef(onUpdateFilter);
  onUpdateFilterRef.current = onUpdateFilter;

  useEffect(() => {
    setSearchInput(filters.searchQuery);
  }, [filters.searchQuery]);

  useEffect(() => {
    if (searchInput === filters.searchQuery) return undefined;
    const timer = setTimeout(() => {
      onUpdateFilterRef.current("search", searchInput);
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [searchInput, filters.searchQuery]);

  const sortValue = `${filters.sortBy || "createdAt"}:${filters.sortDir || "desc"}`;
  const activeCount = countActiveFilters(filters);

  return (
    <div className="filters-panel" role="search" aria-label="Search and filter tasks">
      <div className="filters-bar">
        <input
          type="search"
          className="filter-input"
          aria-label="Search tasks"
          placeholder="Search by title, description, or assignee..."
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <select
          className="filter-select"
          aria-label="Filter by status"
          value={filters.statusFilter}
          onChange={(e) => onUpdateFilter("status", e.target.value)}
        >
          <option value="">All statuses</option>
          <option value="Pending">Pending</option>
          <option value="InProgress">In Progress</option>
          <option value="Completed">Completed</option>
        </select>
        <select
          className="filter-select"
          aria-label="Filter by priority"
          value={filters.priorityFilter}
          onChange={(e) => onUpdateFilter("priority", e.target.value)}
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
          value={filters.categoryFilter}
          onChange={(e) => onUpdateFilter("categoryId", e.target.value)}
        >
          <option value="">All categories</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <select
          className="filter-select"
          aria-label="Filter by assignee"
          value={filters.assigneeFilter}
          onChange={(e) => onUpdateFilter("assignedToId", e.target.value)}
        >
          <option value="">All assignees</option>
          {assignees.map((u) => (
            <option key={u.id} value={u.id}>
              {u.fullName}
            </option>
          ))}
        </select>
      </div>

      <div className="filters-bar filters-bar-secondary">
        <label className="filter-date-label">
          <span>Due from</span>
          <input
            type="date"
            className="filter-select"
            aria-label="Due date from"
            value={filters.dueFrom}
            onChange={(e) => onUpdateFilter("dueFrom", e.target.value)}
          />
        </label>
        <label className="filter-date-label">
          <span>Due to</span>
          <input
            type="date"
            className="filter-select"
            aria-label="Due date to"
            value={filters.dueTo}
            onChange={(e) => onUpdateFilter("dueTo", e.target.value)}
          />
        </label>
        <label className="filter-check">
          <input
            type="checkbox"
            checked={filters.overdueOnly}
            onChange={(e) => onUpdateFilter("overdueOnly", e.target.checked ? "true" : "")}
          />
          <span>Overdue only</span>
        </label>
        <select
          className="filter-select"
          aria-label="Sort tasks"
          value={sortValue}
          onChange={(e) => {
            const [nextSortBy, nextSortDir] = e.target.value.split(":");
            onUpdateSort(nextSortBy, nextSortDir);
          }}
        >
          {SORT_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>
              Sort: {opt.label}
            </option>
          ))}
        </select>
        {activeCount > 0 && (
          <button
            type="button"
            className="btn-ghost btn-sm"
            onClick={onClearFilters}
          >
            Clear filters ({activeCount})
          </button>
        )}
      </div>
    </div>
  );
}

