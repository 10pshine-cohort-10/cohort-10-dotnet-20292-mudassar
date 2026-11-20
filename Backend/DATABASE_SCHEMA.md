# TaskFlow SQL Server Database Schema & ER Diagram

## Entity-Relationship Diagram

```mermaid
erDiagram
    AspNetRoles {
        uniqueidentifier Id PK
        nvarchar_256 Name
        nvarchar_256 NormalizedName UK
        nvarchar_max ConcurrencyStamp
    }

    AspNetUserRoles {
        uniqueidentifier UserId PK, FK
        uniqueidentifier RoleId PK, FK
    }

    AspNetUsers {
        uniqueidentifier Id PK
        nvarchar_100 FullName "Required, Max 100"
        nvarchar_256 UserName
        nvarchar_256 NormalizedUserName UK
        nvarchar_256 Email
        nvarchar_256 NormalizedEmail
        nvarchar_max PasswordHash
        bit IsActive "Default true"
        datetime2 CreatedAt "UTC"
    }

    Categories {
        int Id PK
        nvarchar_100 Name UK "Required, Unique Index"
        nvarchar_500 Description "Nullable"
    }

    Tasks {
        int Id PK
        nvarchar_200 Title "Required, Max 200"
        nvarchar_2000 Description "Nullable, Max 2000"
        int Status "0=Pending, 1=InProgress, 2=Completed"
        int Priority "0=Low, 1=Medium, 2=High, 3=Critical"
        datetime2 DueDate "Nullable, Indexed"
        int CategoryId FK "Nullable, Indexed, OnDelete Restrict"
        uniqueidentifier CreatedById FK "Required, OnDelete Restrict"
        uniqueidentifier AssignedToId FK "Nullable, Indexed with Status, OnDelete Restrict"
        datetime2 CreatedAt "UTC"
        datetime2 UpdatedAt "Nullable"
        bit IsDeleted "Soft-Delete Flag, Indexed"
        datetime2 DeletedAt "Nullable"
        rowversion RowVersion "Optimistic Concurrency Token"
    }

    TaskComments {
        int Id PK
        int TaskItemId FK "Required, OnDelete Cascade"
        uniqueidentifier UserId FK "Required, OnDelete Restrict"
        nvarchar_1000 Text "Required, Max 1000"
        datetime2 CreatedAt "UTC"
    }

    TaskHistories {
        int Id PK
        int TaskItemId FK "Required, OnDelete Cascade"
        uniqueidentifier UserId FK "Required, OnDelete Restrict"
        nvarchar_100 Action "Created, Updated, StatusChanged, Deleted"
        nvarchar_1000 OldValue "Nullable"
        nvarchar_1000 NewValue "Nullable"
        datetime2 Timestamp "UTC"
    }

    AspNetUsers ||--o{ AspNetUserRoles : "has"
    AspNetRoles ||--o{ AspNetUserRoles : "assigned to"

    AspNetUsers ||--o{ Tasks : "creates (CreatedById - Restrict)"
    AspNetUsers |o--o{ Tasks : "is assigned (AssignedToId - Restrict)"
    Categories |o--o{ Tasks : "categorizes (CategoryId - Restrict)"

    Tasks ||--o{ TaskComments : "has comments (Cascade)"
    AspNetUsers ||--o{ TaskComments : "writes (UserId - Restrict)"

    Tasks ||--o{ TaskHistories : "logs audit history (Cascade)"
    AspNetUsers ||--o{ TaskHistories : "performs action (UserId - Restrict)"
```

## Soft-Delete Guarantee (Tracked AppDbContext Deletions)

- **Scope & Limitations:** This guarantee applies to tracked entity deletions saved through `AppDbContext.SaveChanges` and `AppDbContext.SaveChangesAsync`. Set-based bulk operations (such as `ExecuteDelete` / `ExecuteDeleteAsync`) and direct SQL operations (such as `ExecuteSqlRaw` / `ExecuteSqlInterpolated`) bypass the EF Core change tracker and are explicitly excluded from this guarantee.
- **Global Query Filter (`HasQueryFilter(t => !t.IsDeleted)`):** All normal queries on `Tasks`, `TaskComments`, and `TaskHistories` automatically filter out soft-deleted tasks.
- **`SaveChanges` / `SaveChangesAsync` Interceptor (`ConvertTaskDeletesToSoftDelete`):**
  - Even if code calls `db.Tasks.Remove(task)` on a tracked entity, `AppDbContext` intercepts `EntityState.Deleted`, restores pending non-deletion property values (including the original `RowVersion` concurrency token) from `OriginalValues`, transitions the entry through `EntityState.Unchanged`, and marks only `IsDeleted = true`, `DeletedAt = DateTime.UtcNow`, and `UpdatedAt = DateTime.UtcNow` as modified.
  - Deferred cascade/orphan timing (`CascadeTiming.OnSaveChanges`) and `RevertDeletedDependents` resolve pending orphan/cascade transitions and restore tracked `TaskComments` and `TaskHistories` (and their relationships) so they are preserved when a tracked task is soft-deleted through `AppDbContext`.
