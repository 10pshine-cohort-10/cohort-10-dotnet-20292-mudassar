# TaskFlow — Full-Stack Task Management System

TaskFlow is a full-stack task management application built with **ASP.NET Core (.NET 10)**, **Entity Framework Core + SQL Server**, **JWT Authentication & Role-Based Access Control (RBAC)**, **Serilog**, **xUnit**, **SonarQube**, and a **React 18 + Vite** single-page frontend.

---

## Tech Stack

| Layer | Technologies |
|---|---|
| **Backend API** | .NET 10, ASP.NET Core Web API, ASP.NET Core Identity, JWT Bearer Auth, Swagger / Swashbuckle |
| **Database & ORM** | SQL Server, Entity Framework Core 10, Fluent API configurations, Soft-Delete interceptor, `RowVersion` optimistic concurrency |
| **Logging & Error Handling** | Serilog (Async Console + Rolling Daily File sinks), RFC 7807 `ProblemDetails` global exception middleware |
| **Frontend** | React 18, Vite 5, React Router 6, Axios, React Hook Form, Zod, CSS custom properties (Light/Dark theme) |
| **Testing** | xUnit, Moq, `WebApplicationFactory<Program>`, EF Core InMemory & SQLite in-memory (106 unit & integration tests) |
| **Code Quality & CI** | Roslyn Analyzers (`Directory.Build.props`), `.editorconfig` (SonarAnalyzer C# rules), ESLint (SonarJS-aligned rules), SonarQube + GitHub Actions |

---

## Complete Directory Structure

```text
cohort-10-dotnet-20292-mudassar/
├── .coderabbit.yaml                                  # CodeRabbit PR review configuration
├── .editorconfig                                     # C# & JS formatting + SonarAnalyzer / Roslyn rules
├── .env.example                                      # Environment variable template (copy to .env)
├── .gitignore                                        # Git ignore rules (.env, bin/obj, logs, node_modules, TestResults)
├── .github/
│   └── workflows/
│       └── sonarqube.yml                             # GitHub Actions CI: build, xUnit + coverage/TRX, ESLint, Vite build, SonarQube scan
├── docker-compose.sonarqube.yml                      # Local SonarQube LTS Community + PostgreSQL 16 (127.0.0.1:9000)
├── project_readme.md                                 # Full project documentation (this file)
├── README.md                                         # Repository overview
├── scripts/
│   └── run-sonar.ps1                                 # Local SonarQube Scanner runner for .NET + OpenCover/TRX
│
├── Backend/
│   ├── DATABASE_SCHEMA.md                            # Mermaid ER diagram & tracked soft-delete guarantee docs
│   ├── Directory.Build.props                         # Enables .NET analyzers & code-style enforcement in build
│   ├── TaskManager.sln                               # .NET solution (TaskManager.Api + TaskManager.Tests)
│   │
│   ├── src/TaskManager.Api/
│   │   ├── TaskManager.Api.csproj                    # API project & NuGet packages
│   │   ├── Program.cs                                # Composition root, Serilog bootstrap, middleware pipeline, seeding
│   │   ├── appsettings.json                          # Serilog sinks, CORS, JWT, RateLimiting, Seed config defaults
│   │   ├── Properties/
│   │   │   └── launchSettings.json                   # Local launch URLs (https://localhost:5001, http://localhost:5000)
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs                     # /api/auth (register, login, me, update profile, logout)
│   │   │   ├── CategoriesController.cs               # /api/categories (CRUD, Admin-only writes)
│   │   │   ├── DashboardController.cs                # /api/dashboard/summary
│   │   │   ├── TasksController.cs                    # /api/tasks (paged list, detail, create, update, status patch, soft delete)
│   │   │   └── UsersController.cs                    # /api/users (Admin user list/detail/role/active + /lookup)
│   │   ├── DTOs/
│   │   │   ├── AuthDtos.cs                           # Register, Login, AuthResponse, UpdateProfile, UserInfo DTOs
│   │   │   ├── CategoryDtos.cs                       # CategoryDto, SaveCategoryDto
│   │   │   ├── DashboardDtos.cs                      # DashboardSummaryDto
│   │   │   ├── TaskDtos.cs                           # CreateTaskDto, UpdateTaskDto, UpdateTaskStatusDto, TaskDto, PagedResult
│   │   │   └── UserDtos.cs                           # UserDto, UserLookupDto, ChangeRoleDto, SetActiveDto
│   │   ├── Entities/
│   │   │   ├── ApplicationUser.cs                    # IdentityUser<Guid> + FullName, IsActive, CreatedAt
│   │   │   ├── Category.cs                           # Task category entity
│   │   │   ├── Roles.cs                              # Role constants (Admin, User)
│   │   │   ├── TaskComment.cs                        # Task comment entity
│   │   │   ├── TaskEnums.cs                          # TaskItemStatus & TaskPriority enums
│   │   │   ├── TaskHistory.cs                        # Task audit trail entity
│   │   │   └── TaskItem.cs                           # Core task entity with soft-delete & RowVersion concurrency token
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs                       # IdentityDbContext + SaveChanges soft-delete interceptor
│   │   │   ├── DesignTimeDbContextFactory.cs         # EF Core design-time factory for migrations
│   │   │   ├── SeedData.cs                           # Idempotent seeding for roles, admin/user accounts, default categories
│   │   │   ├── Configurations/
│   │   │   │   ├── ApplicationUserConfiguration.cs   # Fluent config for AspNetUsers
│   │   │   │   ├── CategoryConfiguration.cs          # Unique Name index & lengths
│   │   │   │   ├── TaskCommentConfiguration.cs       # Query filter & FK cascade/restrict rules
│   │   │   │   ├── TaskHistoryConfiguration.cs       # Query filter & FK cascade/restrict rules
│   │   │   │   └── TaskItemConfiguration.cs          # Indexes, soft-delete filter, RowVersion, FK rules
│   │   │   └── Migrations/
│   │   │       ├── 20261113134453_AddIdentity.cs
│   │   │       ├── 20261113134453_AddIdentity.Designer.cs
│   │   │       ├── 20261113180116_AddTaskDomain.cs
│   │   │       ├── 20261113180116_AddTaskDomain.Designer.cs
│   │   │       └── AppDbContextModelSnapshot.cs
│   │   ├── Services/
│   │   │   ├── AuthService.cs                        # Registration, lockout/timing-safe login, profile update
│   │   │   ├── CategoryService.cs                    # Category CRUD with case-insensitive uniqueness & FK checks
│   │   │   ├── DashboardService.cs                   # Role-scoped status summary counts
│   │   │   ├── JwtTokenService.cs                    # HMAC-SHA256 JWT generation with SecurityStamp & JTI claims
│   │   │   ├── TaskService.cs                        # Task CRUD, ownership checks, paging/filtering, optimistic concurrency
│   │   │   └── UserService.cs                        # User management, last-active-admin lock/transaction guard, token revocation
│   │   ├── Exceptions/
│   │   │   └── AppExceptions.cs                      # AppValidation, Unauthorized, Forbidden, NotFound, Conflict exceptions
│   │   ├── Extensions/
│   │   │   ├── AuthExtensions.cs                     # Identity, JWT Bearer (OnTokenValidated/401/403), RBAC policies, RateLimiter
│   │   │   ├── ClaimsPrincipalExtensions.cs          # GetUserId() & IsAdmin() helpers
│   │   │   ├── EnvFileLoader.cs                      # Async & sync .env loader
│   │   │   ├── ProblemWriter.cs                      # RFC 7807 ProblemDetails writer for auth events
│   │   │   └── SwaggerExtensions.cs                  # OpenAPI + Bearer JWT Swagger configuration
│   │   └── Middleware/
│   │       ├── GlobalExceptionMiddleware.cs          # Maps exceptions & DbUpdateConcurrencyException to ProblemDetails
│   │       └── UserLogContextMiddleware.cs           # Enriches Serilog LogContext with authenticated UserId & TraceId
│   │
│   └── tests/TaskManager.Tests/
│       ├── TaskManager.Tests.csproj                  # xUnit, Moq, Coverlet, SQLite, InMemory, Mvc.Testing
│       ├── Helpers/
│       │   └── TestEnv.cs                            # Async `.env` loader for test credentials & JWT settings
│       ├── Controllers/
│       │   └── ControllersUnitTests.cs               # Controller status codes, claims mapping, constructor null guards
│       ├── Data/
│       │   └── AppDbContextTests.cs                  # SQLite schema tests: soft delete, query filters, FK restrict, cascade
│       ├── Middleware/
│       │   ├── GlobalExceptionMiddlewareTests.cs     # ProblemDetails mapping, CORS header preservation, stack trace hiding
│       │   └── UserLogContextMiddlewareTests.cs      # Serilog LogContext enrichment tests
│       ├── Services/
│       │   ├── AuthServiceTests.cs                   # Auth rules, duplicate email, lockout, concurrency retry
│       │   ├── CategoryServiceTests.cs               # Category CRUD, duplicate name, referenced-category conflict
│       │   ├── JwtTokenServiceTests.cs               # JWT expiry, identity/role claims, unique JTI
│       │   ├── TaskServiceTests.cs                   # Ownership, search/filters, past due date, RowVersion concurrency
│       │   └── UserServiceTests.cs                   # Role/active changes, self-demotion & last-admin protection
│       └── Integration/
│           ├── TestApiFactory.cs                     # WebApplicationFactory<Program> with InMemory DB & Testing env
│           ├── AuthEndpointsTests.cs                 # Full HTTP auth flows, 401 ProblemDetails, lockout
│           ├── CategoriesEndpointsTests.cs           # Admin/User RBAC & category CRUD over HTTP
│           ├── DashboardEndpointsTests.cs            # User-scoped vs Admin summary counts over HTTP
│           ├── TasksEndpointsTests.cs                # Full Task CRUD, concurrency 409, filters, soft delete over HTTP
│           └── UsersEndpointsTests.cs                # User admin flows, instant JWT revocation on role/active change
│
└── Frontend/
    ├── .eslintrc.cjs                                 # SonarJS-aligned ESLint rules (complexity, eqeqeq, no-eval, etc.)
    ├── .gitignore                                    # Frontend ignores
    ├── README.md                                     # Frontend architecture & auth flow guide
    ├── env.example                                   # Frontend env template
    ├── index.html                                    # Vite HTML entry
    ├── package.json                                  # Scripts: dev, build, preview, lint, sonar
    ├── package-lock.json
    ├── sonar-project.properties                      # Standalone frontend SonarScanner config
    ├── vite.config.js                                # Vite dev server (:5173) & validated /api proxy
    ├── public/
    │   └── favicon.svg
    └── src/
        ├── main.jsx                                  # React root (BrowserRouter, ThemeProvider, AuthProvider)
        ├── App.jsx                                   # Routes, PublicOnly & ProtectedRoute guards
        ├── assets/
        │   └── auth-hero.svg
        ├── api/
        │   ├── axiosClient.js                        # Axios instance, account-bound token interceptor, conditional 401 cleanup
        │   ├── authApi.js                            # /auth API calls
        │   ├── categoriesApi.js                      # /categories API calls
        │   ├── dashboardApi.js                       # /dashboard/summary API call
        │   ├── tasksApi.js                           # /tasks API calls
        │   └── usersApi.js                           # /users API calls
        ├── auth/
        │   ├── AuthContext.jsx                       # Session state, restore/retry, login, shared in-flight logout, cross-tab sync
        │   ├── ProtectedRoute.jsx                    # Route guard & shared SessionRecoveryView component
        │   └── tokenStorage.js                       # IndexedDB + memory fallback + BroadcastChannel token store
        ├── components/
        │   ├── AppLayout.jsx                         # Authenticated shell (Navbar + Outlet)
        │   ├── AuthLayout.jsx                        # Split auth layout for Login & Signup
        │   ├── ErrorAlert.jsx                        # Dismissible error/success alert banner
        │   ├── Field.jsx                             # Accessible form input with password toggle & error binding
        │   ├── Navbar.jsx                            # Responsive navigation bar, role badge, logout, theme toggle
        │   ├── PageHeader.jsx                        # Page title, subtitle, and primary action header
        │   ├── PriorityBadge.jsx                     # Color-coded priority pill (Low/Medium/High/Critical)
        │   ├── StatusBadge.jsx                       # Color-coded status pill (Pending/InProgress/Completed)
        │   ├── SummaryCard.jsx                       # Dashboard KPI metric card
        │   └── ThemeToggle.jsx                       # Light / Dark theme toggle button
        ├── pages/
        │   ├── ComingSoon.jsx                        # Placeholder page component
        │   ├── Dashboard.jsx                         # Status KPI cards + recent tasks table
        │   ├── Login.jsx                             # Login form (React Hook Form + Zod)
        │   ├── Profile.jsx                           # User details, inline Full Name editor, logout
        │   ├── Signup.jsx                            # Registration form with password strength validation
        │   ├── TaskDetail.jsx                        # Full task details, quick status switch, edit/delete actions
        │   ├── TaskForm.jsx                          # Create & Edit task form with lookup retry & retained options
        │   └── TaskList.jsx                          # Search, status/priority/category filters, quick status, pagination
        ├── styles/
        │   ├── auth.css                              # Auth pages styling
        │   └── dashboard.css                         # App layout, tables, cards, alerts, responsive styles
        ├── theme/
        │   └── ThemeContext.jsx                      # Light/Dark theme state persisted to localStorage
        └── utils/
            ├── errorMessage.js                       # Extracts user-friendly messages from ProblemDetails / network errors
            ├── formatDate.js                         # Date formatting helper
            └── pageCache.js                          # Session-scoped client page cache
```

---

## How to Run the Project

### Prerequisites
- **.NET 10 SDK** (`dotnet --version`)
- **Node.js 20+** and **npm** (`node -v`, `npm -v`)
- **SQL Server** (LocalDB, SQL Server Express, or SQL Server Developer Edition)
- *(Optional)* **Docker Desktop** (only needed if running a local SonarQube dashboard)

---

### Step 1: Configure Environment Variables

Copy `.env.example` to `.env` in the repository root and fill in your SQL Server connection string, a 32+ character JWT signing key, and strong seed passwords:

```powershell
Copy-Item .env.example .env
```

Key variables in `.env`:
```env
ASPNETCORE_ENVIRONMENT=Development
ConnectionStrings__DefaultConnection=Server=localhost;Database=TaskManagerDb;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True
Jwt__Issuer=TaskManager
Jwt__Audience=TaskManagerClient
Jwt__Key=REPLACE_WITH_AT_LEAST_32_CHAR_RANDOM_SECRET_KEY
Jwt__ExpiryMinutes=60
SeedAdmin__Email=admin@taskmanager.local
SeedAdmin__FullName=System Admin
SeedAdmin__Password=StrongAdminPassword!1
SeedUser__Email=user@taskmanager.local
SeedUser__FullName=Test User
SeedUser__Password=StrongUserPassword!1
Cors__Origins__0=http://localhost:5173
RateLimiting__AuthPermitLimit=10
VITE_API_BASE_URL=https://localhost:5001/api
```

---

### Step 2: Run the Backend API (.NET 10)

On startup, `Program.cs` loads `.env`, automatically applies pending EF Core migrations to SQL Server (`MigrateAsync`), and seeds default roles (`Admin`, `User`), the configured admin/test accounts, and default categories (`Work`, `Personal`, `Urgent`).

```powershell
dotnet restore Backend/TaskManager.sln
dotnet run --project Backend/src/TaskManager.Api
```

- **API Base URL:** `https://localhost:5001` (or `http://localhost:5000`)
- **Swagger UI (Development):** `https://localhost:5001/swagger`
- **Health Check:** `GET https://localhost:5001/health`
- **Log Files:** Daily rolling logs are written to `Backend/src/TaskManager.Api/logs/log-YYYYMMDD.txt`

*(Optional — apply migrations manually via EF CLI)*:
```powershell
dotnet ef database update --project Backend/src/TaskManager.Api
```

---

### Step 3: Run the Frontend (React + Vite)

In a second terminal:

```powershell
cd Frontend
npm install
npm run dev
```

- **Frontend URL:** `http://localhost:5173`
- By default, Vite proxies `/api` requests to `https://localhost:5001` (configurable via `VITE_DEV_API_TARGET`).

---

### Step 4: Run Tests & Code Quality Verification

Run from the repository root:

```powershell
# 1. Backend Release build with Roslyn/Sonar analyzers enforced as errors
dotnet build Backend/TaskManager.sln -c Release --no-incremental -warnaserror

# 2. Run all 106 xUnit unit & integration tests (with OpenCover coverage + TRX report)
dotnet test Backend/TaskManager.sln -c Release --no-build --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

# 3. Frontend ESLint quality gate (0 warnings allowed)
npm --prefix Frontend run lint -- --max-warnings=0

# 4. Frontend production bundle build
npm --prefix Frontend run build
```

#### Optional: Run Local SonarQube Dashboard

```powershell
# 1. Start local SonarQube + PostgreSQL on http://localhost:9000
docker compose -f docker-compose.sonarqube.yml up -d

# 2. Run the .NET SonarQube scan (after creating a token in SonarQube UI)
$env:SONAR_HOST_URL = "http://localhost:9000"
$env:SONAR_TOKEN = "<YOUR_SONAR_TOKEN>"
.\scripts\run-sonar.ps1

# 3. Optional: Run standalone Frontend SonarScanner
npm --prefix Frontend run sonar -- "-Dsonar.host.url=http://localhost:9000" "-Dsonar.token=$env:SONAR_TOKEN"
```

---

## What You Will See — Functionalities & Features

### 1. Authentication, Security & Session Management
- **Sign Up (`/signup`):** Full-name, email, and strong password validation (8+ characters, uppercase, lowercase, digit, special character) using Zod + React Hook Form. Public registration always assigns the `User` role and returns a neutral `202 Accepted` acknowledgement to prevent account enumeration.
- **Log In (`/login`):** Issues a signed HMAC-SHA256 JWT containing user identity, roles, `jti`, and `security_stamp` claims. Includes 5-attempt account lockout (15-minute lockout), per-IP rate limiting on auth endpoints, and timing-equalized password checks.
- **Instant Token Revocation:** Every authenticated API request validates the user's active status, current `SecurityStamp`, and up-to-date roles against the database (`OnTokenValidated`). Deactivating a user or changing their role immediately invalidates existing JWTs.
- **Resilient Frontend Session Storage:** Tokens are stored in IndexedDB (with automatic in-memory fallback when IndexedDB is unavailable) and synchronized across browser tabs via `BroadcastChannel`. Includes session recovery UI (`Retry` / `Sign in again`) on transient storage/network errors and account-bound Axios request protection so stale requests never attach a different account's token.

### 2. Dashboard (`/dashboard`)
- **Status Summary KPI Cards:** Displays live counts for **Total**, **Pending**, **In Progress**, and **Completed** tasks.
  - **Standard Users** see metrics for tasks they created or are assigned to.
  - **Admins** see workspace-wide counts across all active (non-deleted) tasks.
- **Recent Tasks Overview:** Quick overview table linking directly into task details.

### 3. Task Management (`/tasks`, `/tasks/new`, `/tasks/:id`, `/tasks/:id/edit`)
- **Search, Filter & Pagination (`/tasks`):** Server-side filtering by search query (title, description, or assignee name), status (`Pending`, `InProgress`, `Completed`), priority (`Low`, `Medium`, `High`, `Critical`), and category, with URL-synced query parameters and pagination controls.
- **Quick Status Updates:** Change task status inline from the task list or detail view (`PATCH /api/tasks/{id}/status`), automatically keeping status filters and cached pages consistent.
- **Create & Edit Tasks (`/tasks/new`, `/tasks/:id/edit`):** Assign priority, status, category, assignee (populated from active users via `/api/users/lookup`), description, and due date (prevents past due dates on creation). Includes lookup retry controls and preserves retained category/assignee options if a lookup request fails.
- **Optimistic Concurrency Protection:** Editing a task sends its `RowVersion` token; if another user modified the task first, the API returns `409 Conflict` (`ProblemDetails`) instead of silently overwriting changes.
- **Ownership & Soft-Delete Guarantee:**
  - Standard users can view/update tasks they created or are assigned to, and only the creator or an `Admin` can delete a task.
  - Deleting a task **never physically removes the row** through `AppDbContext` — `ConvertTaskDeletesToSoftDelete` marks `IsDeleted = true`, records `DeletedAt`, hides it via EF Core global query filters, and preserves associated `TaskComments` and `TaskHistories`.

### 4. Profile & Theme Preferences (`/profile` & Navbar)
- **Profile View & Inline Name Update:** Displays user avatar initials, full name, email, assigned roles, active status, and user ID. Users can edit and save their display name (`PUT /api/auth/me`) with inline validation and session-bound save protection.
- **Light / Dark Theme Toggle:** Switchable from the navigation bar and persisted across reloads via `ThemeContext`.
- **Responsive Navigation:** Collapsible mobile menu with active-route highlighting (`Dashboard`, `Tasks`, `Profile`), role pill (`Admin` / `User`), and synchronized sign-out.

### 5. Backend REST API Reference

| Area | Method & Route | Access | Description |
|---|---|---|---|
| **Health** | `GET /health` | Anonymous | Liveness probe returning `{ "status": "Healthy" }` |
| **Auth** | `POST /api/auth/register` | Anonymous (rate-limited) | Register a new `User` account (`202 Accepted`) |
| **Auth** | `POST /api/auth/login` | Anonymous (rate-limited) | Authenticate and receive JWT + user info |
| **Auth** | `GET /api/auth/me` | Authenticated | Fetch current user profile |
| **Auth** | `PUT /api/auth/me` | Authenticated | Update current user's full name |
| **Auth** | `POST /api/auth/logout` | Authenticated | Log sign-out event (`204 No Content`) |
| **Dashboard** | `GET /api/dashboard/summary` | Authenticated | Status counts (own tasks for `User`, all tasks for `Admin`) |
| **Tasks** | `GET /api/tasks` | Authenticated | Paged, filtered task list (`page`, `pageSize`, `status`, `priority`, `categoryId`, `assignedToId`, `search`) |
| **Tasks** | `GET /api/tasks/{id}` | Owner, Assignee, or Admin | Get single task details |
| **Tasks** | `POST /api/tasks` | Authenticated | Create a new task |
| **Tasks** | `PUT /api/tasks/{id}` | Owner, Assignee, or Admin | Full task update with `RowVersion` concurrency check |
| **Tasks** | `PATCH /api/tasks/{id}/status` | Owner, Assignee, or Admin | Quick status transition |
| **Tasks** | `DELETE /api/tasks/{id}` | Owner or Admin | Soft-delete task and record audit history |
| **Categories** | `GET /api/categories` | Authenticated | List categories ordered by name with active task counts |
| **Categories** | `GET /api/categories/{id}` | Authenticated | Get single category |
| **Categories** | `POST /api/categories` | Admin | Create category (case-insensitive uniqueness) |
| **Categories** | `PUT /api/categories/{id}` | Admin | Update category |
| **Categories** | `DELETE /api/categories/{id}` | Admin | Delete category (`409 Conflict` if referenced by tasks) |
| **Users** | `GET /api/users` | Admin | List users with optional `search` and `activeOnly` filters |
| **Users** | `GET /api/users/lookup` | Authenticated | Active user ID/name list for task assignment dropdowns |
| **Users** | `GET /api/users/{id}` | Admin | Get single user details |
| **Users** | `PUT /api/users/{id}/role` | Admin | Change user role (blocks self-demotion & demoting last active Admin) |
| **Users** | `PUT /api/users/{id}/active` | Admin | Activate/deactivate user (blocks self-deactivation & deactivating last active Admin) |

### 6. Observability & Error Handling
- **RFC 7807 `ProblemDetails`:** All expected domain errors (`400`, `401`, `403`, `404`, `409`) and unhandled server errors (`500`) return consistent `application/problem+json` payloads with `traceId`, preserving CORS headers and hiding stack traces outside Development.
- **Structured Logging (Serilog):** Every HTTP request is logged once with final status code, latency, `UserId`, and `TraceId` through non-blocking Async Console and daily rolling File sinks.

