# CLAUDE.md — Travelling Managment Project Memory

## Project Overview

**Pixel.Tasks** is a **.NET 9 MVC Web Application** (server-rendered with Razor Views) for managing tasks across multiple corporations (e.g., Hotel1, Hotel2). A task creator can create tasks, assign them to employees within specific corporations/sections, and track progress. Tasks support three behavioral modes: Normal (plain), Point (checklist), and Counter (numeric progress). An internal comment system (text/image/file) enables communication between task creators and assignees.

**Solution file:** `Pixel.Tasks.sln`  
**Target framework:** `net9.0`  
**Database:** SQL Server (`TasksDB`) via EF Core 9  
**Auth:** ASP.NET Identity (cookie-based, MVC — NOT JWT)  
**Logging:** Serilog (console + daily rolling files in `Tasks.Presentation/Logs/`)  
**Mapping:** AutoMapper  
**UI Template:** Skote — Admin & Dashboard Template (Bootstrap 5, dark sidebar, Boxicons, MetisMenu, SimpleBar, Toastr)  
**UI Preference:** Client prefers modern/professional UI/UX — use modern elements, creative touches, premium feel  

---

## Architecture — Onion / Clean Architecture

```
Tasks.Domain  (Core — no dependencies on other projects)
    ├── Models/            (BaseModel, ICodedEntity, domain entities)
    ├── Models/Identity/   (AppUser : IdentityUser)
    ├── Enums/             (Gender, TaskCategory, PriorityLevel, WorkTaskStatus, CommentType, NotificationType)
    ├── Authorization/     (Roles, Permissions, RolePermissions — static RBAC constants)
    ├── Repositories/      (IGenericRepository<T>)
    ├── Services/          (INTERFACES ONLY: ICodeGeneratorService, INotificationService, IRealtimeNotificationPublisher, IWorkTaskService, IFileStorageService)
    │   └── Contracts/     (service I/O DTOs: TaskPointInput, ProgressResult — records, not entities)
    ├── Rules/             (WorkTaskStatusCalculator — static, pure task-completion rules; concrete domain logic, not an abstraction)
    ├── Specifications/    (ISpecifications<T>, BaseSpecifications<T>, per-entity spec folders)
    └── IUnitOfWork.cs

Tasks.Repository  (Infrastructure — references Tasks.Domain)
    ├── Data/TaskContext.cs               (IdentityDbContext<AppUser>)
    ├── Data/Configrations/               (Fluent API per entity — note: "Configrations" typo is established, keep it)
    ├── Data/DataSeed/                    (empty — seed data is in AppIdentityDbContextSeed.cs)
    ├── Data/AppIdentityDbContextSeed.cs  (seeds 4 users: admin, abdelrahman, khaled, omar)
    ├── Data/Migrations/
    ├── GenericRepository.cs
    ├── SpecificationEvalutor.cs          (note: typo is intentional — keep it)
    ├── Storage/FileStorageService.cs     (IFileStorageService impl — disk-backed file upload/delete; base path injected at startup)
    └── UnitOfWork.cs

Tasks.Services  (Business Logic — references Tasks.Domain)
    ├── CodeGeneration/CodeGeneratorService.cs  (generates sequential codes like PXC-000001)
    ├── Notifications/NotificationService.cs    (creates/persists notifications + pushes via publisher)
    └── WorkTasks/WorkTaskService.cs            (task workflow: create/edit/delete, progress, review, comments — orchestration + notifications)

Tasks.Presentation  (MVC Web App — references Tasks.Repository + Tasks.Services)
    ├── Controllers/       (AccountController, HomeController, CorporationController, SectionController, TaskTypeController, UserController, TaskController, NotificationController)
    ├── Hubs/              (NotificationHub — SignalR real-time notification channel)
    ├── ViewModels/        (LoginViewModel, CorporationViewModel, SectionViewModel, TaskTypeViewModel, UserViewModel, AvailableEmployeeViewModel, WorkTaskViewModel, TaskPointViewModel, TaskCommentViewModel, CommentsPanelViewModel, MyTaskViewModel, TaskWorkViewModel, AssignmentProgressViewModel, NotificationViewModel, ErrorViewModel)
    ├── Views/             (Razor views organized per controller + Shared layout partials)
    ├── MappingProfiles/   (CorporationProfile, SectionProfile, TaskTypeProfile, WorkTaskProfile, NotificationProfile)
    ├── Factories/         (IWorkTaskViewModelFactory + WorkTaskViewModelFactory — builds every WorkTask view model so TaskController stays HTTP-only; IDashboardViewModelFactory + DashboardViewModelFactory — builds the admin/employee home dashboards so HomeController stays HTTP-only)
    ├── Validation/        (WorkTaskValidator — static task-type create/edit rules)
    ├── Authorization/     (PermissionRequirement, PermissionAuthorizationHandler, PermissionPolicyProvider, AppUserClaimsPrincipalFactory)
    ├── Services/          (SignalRNotificationPublisher — IRealtimeNotificationPublisher impl via IHubContext)
    ├── TagHelpers/        (PermissionTagHelper — <permission required="..."> suppresses output for unauthorized users)
    ├── Helpers/           (AvatarHelper — resolves user avatar by gender; NotificationDisplay; TaskStatusBadge; TaskPriorityBadge; CommentTypeDisplay — comment-type Boxicon for dashboard feeds; DeadlineBadge — dashboard due-date badge (overdue/today/soon)). File upload/delete is now IFileStorageService (Domain abstraction, impl in Tasks.Repository).
    ├── Logs/              (Serilog daily rolling log files)
    ├── wwwroot/           (Skote template assets: css, js, lib, back/)
    ├── Program.cs         (startup, DI, pipeline)
    └── appsettings.json
```

### Dependency Flow

```
Tasks.Presentation → Tasks.Services  → Tasks.Domain
Tasks.Presentation → Tasks.Repository → Tasks.Domain
```

**Key rule:** `Tasks.Domain` has ZERO project references to Repository, Service, or Presentation. It only has NuGet: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.

---

## NuGet Packages by Project

| Project | Key Packages |
|---|---|
| **Tasks.Domain** | `Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.17` |
| **Tasks.Repository** | `Microsoft.EntityFrameworkCore.SqlServer 9.0.17` (references Tasks.Domain) |
| **Tasks.Services** | _(no NuGet — only project reference to Tasks.Domain)_ |
| **Tasks.Presentation** | `AutoMapper.Extensions.Microsoft.DependencyInjection 12.0.1`, `Serilog.AspNetCore 10.0.0`, `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation 9.0.17`, `Microsoft.EntityFrameworkCore.Tools 9.0.17`, `Microsoft.VisualStudio.Web.CodeGeneration.Design 9.0.12` |

> **SignalR** (used for real-time notifications) ships in the ASP.NET Core shared framework — no server-side NuGet needed (`builder.Services.AddSignalR()`). The **browser client** is loaded from CDN (`microsoft-signalr/8.0.0/signalr.min.js`) in `_Scripts.cshtml`, matching how Toastr is loaded.

---

## Domain Models

### Base

- **`BaseModel`** — `int Id` (all DB entities inherit from this)
- **`ICodedEntity`** — marker interface with `string Code { get; set; }` for entities with auto-generated sequential codes

### Identity

- **`AppUser`** : `IdentityUser` — `FirstName`, `LastName`, `FullName` (computed), `Gender` (enum), `IsActive` (default true), `ImageUrl?`, `int? CorporationId`, `int? SectionId`  
  - CorporationId/SectionId are **nullable** — admins/task creators may not belong to any corporation  
  - Navigation: `Corporation?`, `Section?`

### Core Entities

- **`Corporation`** : `BaseModel, ICodedEntity` — `Name`, `NameAr?`, `Code` (unique, auto-generated: PXC-######), `Notes?`  
  Navigation: `ICollection<Section>`, `ICollection<AppUser>`

- **`Section`** : `BaseModel, ICodedEntity` — `Name`, `Code` (unique), `Email?`, `Fax?`, `Phone?`, `Address?`, `Telex?`, `Notes?`, `int CorporationId`  
  Navigation: `Corporation`, `ICollection<AppUser>`

- **`TaskType`** : `BaseModel` — `Name` (unique), `TaskCategory Category` (enum)  
  Admin-managed lookup table. Navigation: `ICollection<WorkTask>`

### Task System

- **`WorkTask`** : `BaseModel, ICodedEntity` — `Title`, `Code` (unique), `Description?`, `Notes?`, `RequestDate`, `DueDate`, `PriorityLevel Priority`, `WorkTaskStatus Status` (default Pending), `int? TargetCount` (Counter-type only), `int TaskTypeId`, `string CreatedByUserId`, `int CorporationId`, `int? SectionId` (null = targets all sections in corp)  
  Navigation: `TaskType`, `CreatedBy (AppUser)`, `Corporation`, `Section?`, `ICollection<TaskAssignment>`, `ICollection<TaskPoint>`, `ICollection<TaskComment>`

- **`TaskAssignment`** : `BaseModel` — `WorkTaskStatus Status` (default Pending), `int? CompletedCount` (Counter-type per-user progress), `DateTime AssignedAt`, `int WorkTaskId`, `string UserId`  
  Unique constraint: `(WorkTaskId, UserId)`. Navigation: `WorkTask`, `User (AppUser)`, `ICollection<TaskPointStatus>`

- **`TaskPoint`** : `BaseModel` — `Description`, `int Order` (display order, rearrangeable), `int WorkTaskId`  
  Navigation: `WorkTask`, `ICollection<TaskPointStatus>`

- **`TaskPointStatus`** : `BaseModel` — `bool IsCompleted` (default false), `DateTime? CompletedAt`, `int TaskAssignmentId`, `int TaskPointId`  
  Unique constraint: `(TaskAssignmentId, TaskPointId)`. Tracks per-user per-point completion independently.

- **`TaskComment`** : `BaseModel` — `Content?` (text), `FileUrl?` (image/file path), `CommentType Type`, `DateTime CreatedAt`, `int WorkTaskId`, `string UserId`  
  Each comment is exactly **one type** (text OR image OR file — never multiple).

### Notifications

- **`Notification`** : `BaseModel` — `string RecipientUserId`, `string? ActorUserId` (who triggered it), `NotificationType Type`, `string Title`, `string Message`, `string? Url` (relative deep-link), `bool IsRead` (default false), `DateTime CreatedAt`, `int? WorkTaskId`  
  Navigation: `Recipient (AppUser)`, `Actor (AppUser?)`, `WorkTask?`. **Title/Message/Url are rendered at creation time** (in `NotificationService`) so views render plain stored strings and new types can render however they like.

---

## Enums

| Enum | Values | Location |
|---|---|---|
| `Gender` | `Male=1`, `Female=2` | `Tasks.Domain/Enums/` |
| `TaskCategory` | `Normal=1`, `Point=2`, `Counter=3` | `Tasks.Domain/Enums/` |
| `PriorityLevel` | `Low=1` 🟢`#10B981`, `Medium=2` 🟡`#F59E0B`, `High=3` 🟠`#F97316`, `Critical=4` 🔴`#EF4444` | `Tasks.Domain/Enums/` |
| `WorkTaskStatus` | `Pending=1`, `InProgress=2`, `Completed=3` | `Tasks.Domain/Enums/` |
| `CommentType` | `Text=1`, `Image=2`, `File=3` | `Tasks.Domain/Enums/` |
| `NotificationType` | `TaskAssigned=1`, `NewComment=2`, `TaskNeedsReview=3` | `Tasks.Domain/Enums/` |

---

## Key Patterns

### Generic Repository + Specification

All data access goes through `IGenericRepository<T>` + `ISpecifications<T>`:

```csharp
var spec = new SomeSpecification(params);
var result = await _unitOfWork.Repository<Entity>().GetByIdAsync(spec);
var results = await _unitOfWork.Repository<Entity>().GetAllAsync(spec);
```

**IGenericRepository<T>** methods:
- `GetAllAsync(ISpecifications<T> Spec)` → `IEnumerable<T>`
- `GetByIdAsync(ISpecifications<T> Spec)` → `T`
- `AddAsync(T entity)`
- `Update(T entity)`
- `Delete(T entity)`

**ISpecifications<T>** supports:
- `Criteria` — `Expression<Func<T, bool>>` (WHERE clause)
- `Includes` — `List<Expression<Func<T, object>>>` (eager loading)
- `IncludeStrings` — `List<string>` (string-based ThenInclude chains, e.g., `"Categories.Items"`)
- `OrderBy` / `OrderByDesc`
- `Skip`, `Take`, `IsPaginationEnabled`

**BaseSpecifications<T>** provides:
- Parameterless constructor (get all)
- Constructor with `Expression<Func<T, bool>>` criteria (get by condition)
- Helper methods: `AddInclude(string)`, `SetOrderBy(...)`, `SetOrderByDesc(...)`, `ApplyPagination(skip, take)`

### Unit of Work

```csharp
_unitOfWork.Repository<Entity>()  // Get or create repository
await _unitOfWork.CompleteAsync()  // SaveChangesAsync
```

Uses a `Hashtable` to cache repository instances per entity type name.

#### Transaction Pattern

For methods with **multiple DB operations that must succeed or fail as one unit**, wrap them in an explicit transaction:

```csharp
await _unitOfWork.BeginTransactionAsync();
try
{
    // ... multiple Repository calls + CompleteAsync calls ...
    await _unitOfWork.CommitTransactionAsync();
}
catch
{
    await _unitOfWork.RollbackTransactionAsync();
    throw;
}
```

**When to use transactions:**
- A method calls `CompleteAsync()` more than once (e.g., need a DB-generated Id between saves)
- Two or more unrelated entities must be written atomically

**When NOT to use transactions:**
- A method has only a single `CompleteAsync()` — EF Core's SaveChanges is already atomic

### SpecificationEvalutor

Located in `Tasks.Repository/SpecificationEvalutor.cs` (note: typo in filename is intentional — keep it). Builds the EF Core query from a specification by applying Criteria → OrderBy → OrderByDesc → Pagination → Includes → IncludeStrings.

### Code Generation (ICodeGeneratorService)

Generates sequential unique codes for any `ICodedEntity`. Entity-agnostic — the caller supplies the prefix.

| Entity | Prefix | Example Code |
|---|---|---|
| Corporation | `PXC` | `PXC-000001` |
| Section | `PXS` | `PXS-000001` |
| WorkTask | `PXW` | `PXW-000001` |

Padding: 6-digit zero-padded for 1–999,999; plain number above that.

---

## Existing Specifications

| Specification | Entity | Purpose |
|---|---|---|
| `CorporationSpec()` | `Corporation` | Get all corporations |
| `CorporationSpec(int id)` | `Corporation` | Get corporation by ID |
| `CorporationByNameSpec(string name)` | `Corporation` | Find by exact name (case-insensitive) — used for remote uniqueness validation |
| `SectionSpec()` | `Section` | Get all sections (includes Corporation + Users) |
| `SectionSpec(int id)` | `Section` | Get section by ID (includes Corporation + Users) |
| `SectionByNameSpec(string name)` | `Section` | Find by exact name — remote uniqueness validation |
| `SectionByCorporationSpec(int corporationId)` | `Section` | Get all sections for a corporation (ordered by Name) — used in User create/edit dropdown |
| `TaskTypeSpec()` | `TaskType` | Get all task types |
| `TaskTypeSpec(int id)` | `TaskType` | Get task type by ID |
| `TaskTypeByNameSpec(string name)` | `TaskType` | Find by exact name — remote uniqueness validation |
| `WorkTaskSpec()` | `WorkTask` | All tasks (unfiltered, light graph) — includes TaskType, Corporation, Section, CreatedBy, Assignments; ordered by RequestDate desc. Reserved for a phase-2 Super Admin "see everything" view |
| `WorkTaskSpec(string creatorUserId)` | `WorkTask` | Same light graph filtered to `CreatedByUserId` — admin "Tasks List" Index (admin isolation: each admin sees only their own tasks) |
| `WorkTaskSpec(int id)` | `WorkTask` | Single task full graph (Details/Edit) — includes Points, Assignments.User, Assignments.PointStatuses, Comments.User |
| `WorkTaskByUserSpec(string userId)` | `WorkTask` | Tasks the user is assigned to (employee "My Tasks") |
| `WorkTaskByCreatorSpec(string userId)` | `WorkTask` | Tasks the admin created, heavier graph (Points, Assignments.PointStatuses) — admin "Active Tasks" cards |
| `DashboardWorkTaskSpec()` / `DashboardWorkTaskSpec(string creatorUserId)` | `WorkTask` | Dashboard aggregates; the string overload scopes stats/charts to one admin's created tasks |
| `TaskAssignmentSpec(int workTaskId, string userId)` | `TaskAssignment` | A user's assignment for a task (includes WorkTask.Points, PointStatuses.TaskPoint) |
| `TaskPointsByWorkTaskSpec(int workTaskId)` | `TaskPoint` | A task's points (no includes) — used by `WorkTaskService` when seeding statuses for newly added assignees |
| `TaskCommentSpec(int workTaskId)` | `TaskComment` | All comments for a task, oldest-first, includes User |
| `NotificationByUserSpec(string userId)` | `Notification` | A user's notifications, newest-first, includes Actor |
| `NotificationByUserSpec(string userId, int skip, int take)` | `Notification` | Paginated overload — history page + bell "recent" fetch |
| `UnreadNotificationByUserSpec(string userId)` | `Notification` | A user's unread notifications — badge count + mark-all-read |
| `NotificationByIdSpec(int id)` | `Notification` | Single notification by ID |
| `NotificationByWorkTaskSpec(int workTaskId)` | `Notification` | A task's notifications — cleared before task delete (NoAction FK) |

---

## Service Layer — Business Logic

| Interface | Implementation | Purpose |
|---|---|---|
| `ICodeGeneratorService` | `CodeGeneratorService` | Generates sequential codes (PXC-######) for coded entities |
| `INotificationService` | `NotificationService` (Tasks.Services) | Composes + persists notifications then pushes them; `GetUnreadCountAsync`, `MarkAsReadAsync`, `MarkAllAsReadAsync` |
| `IRealtimeNotificationPublisher` | `SignalRNotificationPublisher` (Tasks.Presentation) | Pushes a notification to its recipient over SignalR — abstraction in Domain, impl in Presentation so the service layer takes no web dependency |
| `IWorkTaskService` | `WorkTaskService` (Tasks.Services) | Task workflow orchestration: `CreateAsync`, `EditAsync`, `DeleteAsync`, `TogglePointAsync`, `UpdateCounterAsync`, `SetNormalStatusAsync`, `MarkAssignmentReviewedAsync`, `AddCommentAsync`. Owns the transactions, point/assignment reconciliation, status recompute (via `WorkTaskStatusCalculator`), and fires notifications after commit |
| _(static)_ `WorkTaskStatusCalculator` (**Tasks.Domain/Rules**) | Pure, dependency-free task-completion rules: `ProgressPercent`, `RecomputeAssignmentStatus`, `RecomputeTaskStatus`, `RecomputeAll`. Reused by `WorkTaskService` (workflow) and `WorkTaskViewModelFactory` (display) |

> **Layer purity:** `Tasks.Domain/Services/` holds **interfaces only**. Concrete pure rules live in `Tasks.Domain/Rules/`; the service's I/O DTOs (`TaskPointInput`, `ProgressResult`) live in `Tasks.Domain/Services/Contracts/`.

**WorkTask workflow flow:** `TaskController` stays HTTP-only — model binding, mapping the inbound form to a `WorkTask` (`IMapper`), `UserManager`/Identity (resolving the acting `AppUser` + assignable employee ids), `CanManageTask` admin-isolation guards, `TempData`/`Json`. It hands already-loaded, request-scoped **tracked** entities plus the `AppUser actor` into `IWorkTaskService`, which persists and notifies. Progress mutations return a `ProgressResult` record the controller shapes into its AJAX JSON. Point drafts cross the boundary as `TaskPointInput` records (never the view model). The service takes **no** `UserManager`/`IMapper`/web types — mirrors `NotificationService`.

**View-model construction** lives entirely in `IWorkTaskViewModelFactory` (`Tasks.Presentation/Factories/`, injects `IUnitOfWork` + `IMapper`) — `BuildTaskList`/`BuildDetails`/`BuildEdit`/`BuildAssignmentProgress`/`BuildMyTasks`/`BuildCreatedTaskCards`/`BuildComments`/`BuildCommentsPanel`/`BuildWork`/`PopulateLookups`. It is the sole **display-side** user of `WorkTaskStatusCalculator`, so the controller never references the calculator. URL building (`Url.Action`) stays in the controller and is passed in as `returnUrl`. Task-type create/edit validation is the static `WorkTaskValidator.ApplyCategoryRules(ModelState, model, category)`; the image-vs-file check is `DocumentSettings.IsImage`. The controller's own privates are now only auth/Identity/data-loading/HTTP shaping (`CanManageTask`, `LoadOwnedAsync`, `ResolveCategoryAsync`, `GetAssignableEmployeesAsync`, `ResolveAssigneeIdsAsync`, `PopulateLookupsAsync` wrapper, `ProgressJson`).

**Notification flow:** `NotificationService` (intent-named methods: `NotifyTaskAssignedAsync`, `NotifyNewCommentAsync`, `NotifyNeedsReviewAsync`) builds Title/Message + a relative deep-link Url, persists the row via `IUnitOfWork`, recomputes the recipient's unread count, then calls `IRealtimeNotificationPublisher.PublishAsync`. It **never notifies the actor about their own action** (`recipientUserId == actorUserId` is skipped). Callers fire notifications **after commit** so a rolled-back transaction never produces a phantom alert. The `"Title" (Code)` fragment shared by every message is built once via the private `TaskRef(task)` helper.

**Adding a new notification type** — exactly three colocated, fail-loud edits:
1. Add the value to `NotificationType` (`Tasks.Domain/Enums/NotificationType.cs`).
2. Add an intent-named `Notify…Async` method to `INotificationService` + `NotificationService` that composes Title/Message/Url and calls the private `CreateAsync`.
3. Add its `(Icon, ColorClass)` row to `NotificationDisplay.For` (`Tasks.Presentation/Helpers/NotificationDisplay.cs`) — the single source of truth for the bell/history icon + colour. A forgotten type throws rather than rendering a generic grey bell. `NotificationViewModel.Icon`/`ColorClass` just delegate here.

---

## AutoMapper Configuration

Profiles in `Tasks.Presentation/MappingProfiles/` (all registered in `Program.cs`): `CorporationProfile`, `SectionProfile`, `TaskTypeProfile`, `WorkTaskProfile`, `NotificationProfile`.

- `Corporation → CorporationViewModel` (for display)
- `CorporationViewModel → Corporation` (for Create/Edit) — `Code` is **ignored** (auto-generated, not mapped from VM)
- **`WorkTaskProfile`**: `WorkTask ↔ WorkTaskViewModel` (maps display fields TaskTypeName/TaskCategory/CorporationName/SectionName/CreatedByName/AssigneeCount; ignores Code, Status, navigation, select lists, Points, SelectedUserIds — those are set manually in the controller). Also `TaskComment → TaskCommentViewModel`.
- **`NotificationProfile`**: `Notification → NotificationViewModel` (maps ActorName/ActorImageUrl/ActorGender; Icon/ColorClass/TimeAgo/AvatarSrc are computed in the VM).

---

## Database

### DbContext — `TaskContext` : `IdentityDbContext<AppUser>`

**DbSets:** `Corporations`, `Sections`, `TaskTypes`, `WorkTasks`, `TaskAssignments`, `TaskPoints`, `TaskPointStatuses`, `TaskComments`, `Notifications`

**Configurations** (Fluent API via `IEntityTypeConfiguration<T>`):
- Located in `Tasks.Repository/Data/Configrations/` (note: "Configrations" typo is established, keep it)
- One config file per entity: `AppUserConfig`, `CorporationConfig`, `SectionConfig`, `TaskTypeConfig`, `WorkTaskConfig`, `TaskAssignmentConfig`, `TaskPointConfig`, `TaskPointStatusConfig`, `TaskCommentConfig`, `NotificationConfig`
- Applied via `modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`

### FK Cascade Strategy (SQL Server)

SQL Server forbids multiple cascade paths reaching the same table. Strategy:

| Relationship | Delete Behavior | Reason |
|---|---|---|
| Corporation → Section | **Cascade** | Section is fully owned by Corporation |
| Section → WorkTask | **NoAction** | Avoids cycle: Corp→Section→WorkTask AND Corp→WorkTask |
| AppUser → Corporation/Section | **NoAction** | Avoids cycle through WorkTask |
| AppUser → WorkTask (creator) | **NoAction** | Avoids cycle via Corporation |
| AppUser → TaskAssignment | **NoAction** | Avoids cycle via WorkTask |
| AppUser → TaskComment | **NoAction** | Avoids cycle via WorkTask |
| WorkTask → TaskAssignment/Point/Comment | **Cascade** | Child records fully owned by task |
| TaskType → WorkTask | **Cascade** | No cycle path through TaskType |
| TaskAssignment → TaskPointStatus | **Cascade** | Fully owned |
| TaskPoint → TaskPointStatus | **NoAction** | Avoids cycle: WorkTask→Assignment→PointStatus AND WorkTask→Point→PointStatus |
| AppUser → Notification (Recipient/Actor) | **NoAction** | Reaches AppUser (reachable from Corporation) — avoids cascade cycle |
| WorkTask → Notification | **NoAction** | WorkTask reachable from Corporation — avoids cascade cycle; `TaskController.Delete` clears the task's notifications manually first |

### Seeding

- **`AppIdentityDbContextSeed`** — Seeds 4 users on first run (admin, abdelrahman, khaled, omar). No roles seeded yet (RBAC planned for later).
- Runs at startup in `Program.cs` after `MigrateAsync()`

### Connection String

```
Server=.;Database=TasksDB;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;
```

---

## Core Business Rules

1. **Corporations** have many **Sections**; sections belong to exactly one corporation.
2. **Users** belong to at most one Corporation and at most one Section (both nullable — admins may be unattached).
3. **Task creation flow:** Select Corporation → (optionally) Section → User(s). If only Corporation is selected (no section), the task targets all users in all sections of that corporation.
4. **Task types** determine behavior: **Normal** (plain), **Point** (checklist with ordered items), **Counter** (numeric target with per-user completed count).
5. **TaskAssignment** has its own `Status` — each assigned user progresses independently. The overall `WorkTask.Status` reflects aggregate state.
6. **Point-type tasks:** Each assigned user independently marks individual points as done — allows partial completion.
7. **Counter-type tasks:** `TargetCount` set on WorkTask; each `TaskAssignment` has its own `CompletedCount`.
8. **Comments** function as internal chat — each comment is exactly one type: text, image, or file.
9. **Admin task isolation:** each admin only sees/manages the tasks they created — list pages (`Index` "Tasks List", `MyCreatedTasks` "Active Tasks") and the admin dashboard are filtered by `CreatedByUserId`, and `Details`/`Edit`/`Delete` enforce it via the `CanManageTask` helper (NotFound otherwise). A future **Super Admin** role (phase 2) will see everything — the rule lives in the single `CanManageTask` helper and the unfiltered `WorkTaskSpec()`/`DashboardWorkTaskSpec()` overloads so the change is one place.

---

## Program.cs — DI Registration & Pipeline

### Services Registered

```csharp
// Mapping
AddAutoMapper(CorporationProfile)

// Data & Repositories
AddDbContext<TaskContext>(SqlServer)
AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>))
AddScoped<IUnitOfWork, UnitOfWork>()

// Business Services
AddScoped<ICodeGeneratorService, CodeGeneratorService>()
AddScoped<INotificationService, NotificationService>()
AddScoped<IRealtimeNotificationPublisher, SignalRNotificationPublisher>()

// Real-time
AddSignalR()                     // hub mapped at /hubs/notifications via app.MapHub<NotificationHub>()

// MVC
AddControllersWithViews() + AddRazorRuntimeCompilation()

// Identity
AddIdentity<AppUser, IdentityRole>(password policy) + AddEntityFrameworkStores<TaskContext>()

// Cookie Auth
LoginPath = /Account/Login, AccessDeniedPath = /Account/AccessDenied, 8h expiry, sliding
```

### Middleware Pipeline

```
ExceptionHandler (prod) → HTTPS → Routing → Authentication → Authorization → StaticAssets → MapHub<NotificationHub> → MapControllerRoute
```

Default route: `{controller=Account}/{action=Login}/{id?}` — starts on Login page.

---

## Controllers & Views

### AccountController
| Action | Method | Auth | Description |
|---|---|---|---|
| `Login` | GET | Anonymous | Show login page (redirects to Home if already authenticated) |
| `Login` | POST | Anonymous | Validate credentials (supports email or username), sign in via cookie |
| `SignOut` | GET | Authorize | Sign out and redirect to Login |
| `AccessDenied` | GET | Anonymous | 403 page shown when a user lacks the required permission |

### HomeController (`[Authorize]`)
| Action | Description |
|---|---|
| `Index` | Role-based dashboard: renders `AdminDashboard` (own-created-task KPIs, charts, workload, leaderboard, overdue feed) for Admins and `EmployeeDashboard` (assigned-task KPIs, progress, deadlines, messages) for Employees. Controller resolves Identity (userId, active-employee count, name) and delegates all view-model building to `IDashboardViewModelFactory`. |
| `Privacy` | Privacy page |
| `Error` | Error page |

### CorporationController (`[Authorize(Policy = Permissions.Corporations.Manage)]`)
| Action | Method | Description |
|---|---|---|
| `Index` | GET | List all corporations (DataTable view) |
| `Details` | GET | View single corporation details |
| `Create` | GET/POST | Create form + save. Code auto-generated via `ICodeGeneratorService` (prefix: `PXC`) |
| `Edit` | GET/POST | Edit form. Code is read-only (never edited) |
| `Delete` | POST | Delete via AJAX, returns JSON `{ success, message }` |
| `CheckUniqueName` | GET | Remote validation — checks corporation name uniqueness (excludes current ID during edit) |

### SectionController (`[Authorize(Policy = Permissions.Sections.Manage)]`)
| Action | Method | Description |
|---|---|---|
| `Index` | GET | List all sections (DataTable) |
| `Details` | GET | View section + assigned employees |
| `Create` | GET/POST | Create + assign employees; code auto-generated (prefix: `PXS`) |
| `Edit` | GET/POST | Edit + reconcile employee assignments |
| `Delete` | POST | Detach employees, then delete; AJAX JSON response |
| `CheckUniqueName` | GET | Remote validation |
| `GetAvailableEmployees` | GET | Returns active unassigned employees for a corporation (AJAX) |

### TaskTypeController (`[Authorize(Policy = Permissions.TaskTypes.Manage)]`)
| Action | Method | Description |
|---|---|---|
| `Index` | GET | List all task types |
| `Details` | GET | View task type |
| `Create` | GET/POST | Create task type |
| `Edit` | GET/POST | Edit task type |
| `Delete` | POST | Delete via AJAX |
| `CheckUniqueName` | GET | Remote validation |

### UserController (`[Authorize(Policy = Permissions.Users.Manage)]`)
| Action | Method | Description |
|---|---|---|
| `Index` | GET | List all users (DataTable) with role/corp/section/status columns |
| `Create` | GET/POST | Create user with role, corp, section, password |
| `Edit` | GET/POST | Edit user info, role, assignment, optional password change |
| `ToggleActive` | POST | Activate/deactivate user; AJAX JSON response |
| `GetSectionsByCorporation` | GET | Returns sections for a corporation dropdown (AJAX) |

### TaskController (`[Authorize]` — per-action permission policies)
The Task feature. Assignments are **materialized per-employee at creation time** (one `TaskAssignment` row each). Task type is **immutable after creation**. Overall status is **auto-computed** from assignments (see business rules). File uploads land in `wwwroot/Files/TaskComments`.

| Action | Method | Policy | Description |
|---|---|---|---|
| `Index` | GET | `Tasks.ViewAll` | Admin DataTable ("Tasks List") of the **current admin's own** tasks only (admin isolation) |
| `Details` | GET | `Tasks.ViewAll` | Admin view: info + per-assignee progress table + comments chat. Guarded by `CanManageTask` → NotFound if not the creator |
| `MyCreatedTasks` | GET | `Tasks.ViewAll` | Admin card view ("Active Tasks") of own created tasks, excluding Reviewed |
| `Create` | GET/POST | `Tasks.Create` | Create form; POST in a transaction → generate `PXW` code → save WorkTask → Points (Point type) → one TaskAssignment per employee → seed TaskPointStatus per (assignment×point) |
| `Edit` | GET/POST | `Tasks.Create` | Descriptive fields free; reconcile assignees & points (removing one deletes its progress); type locked. Guarded by `CanManageTask` → NotFound if not the creator |
| `Delete` | POST | `Tasks.Create` | AJAX JSON; deletes TaskPointStatuses first (NoAction FK) then cascades the rest. Guarded by `CanManageTask` → `{ success:false }` if not the creator |
| `MyTasks` | GET | `Tasks.ViewAssigned` | Employee card list of own tasks with progress % |
| `Work` | GET | `Tasks.ViewAssigned` | Employee working view (points checklist / counter bar / Normal toggle) + chat. Guards assignment ownership |
| `TogglePoint` | POST | `Tasks.UpdateProgress` | AJAX flip a TaskPointStatus, recompute statuses, return progress |
| `UpdateCounter` | POST | `Tasks.UpdateProgress` | AJAX set CompletedCount (clamped), recompute, return progress |
| `SetStatus` | POST | `Tasks.UpdateProgress` | AJAX Normal-type explicit status set (Pending / InProgress / Completed); rejects non-Normal types |
| `AddComment` | POST | `Tasks.Comment` | AJAX text/image/file (exactly one type); file saved via `IFileStorageService.SaveFileAsync`; returns rendered comment |
| `GetSectionsByCorporation` | GET | `Tasks.Create` | Sections-for-corporation dropdown (AJAX) |
| `GetAvailableEmployees` | GET | `Tasks.Create` | Active **employees** (Employee role) in corp filtered by optional section (AJAX) |

> The write-path actions above (`Create`, `Edit`, `Delete`, `TogglePoint`, `UpdateCounter`, `SetStatus`, `MarkAssignmentReviewed`, `AddComment`) delegate their orchestration to `IWorkTaskService` (`WorkTaskService` in Tasks.Services). The controller only loads + guards (`CanManageTask`), maps view models, resolves the `AppUser` actor + assignable employee ids (Identity), and shapes responses. `MarkAssignmentReviewed` also enforces `CanManageTask` (admin isolation) like Details/Edit/Delete.

Status recompute rules live in the static `WorkTaskStatusCalculator` (`Tasks.Domain/Services`), reused by both the controller (progress % for display) and `WorkTaskService` (status transitions): Point assignment Completed when all its points checked; Counter when CompletedCount ≥ TargetCount; Normal set explicitly by `SetStatus` (Pending=0% / InProgress=50% / Completed=100%). Task = Completed when all assignments Completed/Reviewed, Reviewed when all Reviewed, InProgress when any started, else Pending.

**Notification triggers (fired inside `WorkTaskService` *after* commit/save):**
- **Create** → `NotifyTaskAssignedAsync` for each assigned employee (after `CommitTransactionAsync`).
- **Edit** → `ReconcileAssignmentsAsync` returns the *newly added* assignee ids; `NotifyTaskAssignedAsync` for each (after commit).
- **AddComment** → `NotifyNewCommentAsync` to the other party (`isCreator` ? employee : admin; `recipientIsAdmin = !isCreator`). Controller handles the file upload and builds the `TaskComment`; the service persists + notifies.
- **TogglePoint / UpdateCounter / SetStatus** → each captures `previousStatus` before mutating and passes it to the private `SaveProgressAsync`; on a fresh transition into `Completed`, `NotifyNeedsReviewAsync` alerts the task creator.
- **Delete** → clears the task's notifications (`NotificationByWorkTaskSpec`) before deleting the task (NoAction FK).

### NotificationController (`[Authorize]` — every authenticated user; no special permission)
In-app notifications. SignalR's default user-id provider maps to the `NameIdentifier` claim (= AppUser.Id), so `Clients.User(userId)` reaches the right user with no group bookkeeping.

| Action | Method | Description |
|---|---|---|
| `Index` | GET | Full history page (paged, `PageSize = 20`) for the current user |
| `Recent` | GET | JSON: latest ~10 + unread count — seeds the bell dropdown on page load |
| `Open` | GET | Marks one notification read, then redirects to its (local-validated) `Url` |
| `MarkAllAsRead` | POST | AJAX JSON; clears the badge |

**Front-end:** the Skote bell in `_Nav.cshtml` (button `#page-header-notifications-dropdown`) shows a live `#notification-badge`, a `#notification-list`, a "Mark all as read" action, and a "View all" footer → `Index`. `wwwroot/back/assets/js/notifications.js` opens the `/hubs/notifications` connection, calls `Recent` on load, handles the `ReceiveNotification` event (prepend + bump badge + Toastr), and wires mark-all-read (reads the `__RequestVerificationToken` from `@Html.AntiForgeryToken()` rendered in `_Nav`). HTML in the JS is escaped to avoid XSS from user-supplied names.

### Views Structure

```
Views/
├── Account/Login.cshtml, AccessDenied.cshtml  (Skote auth layout via _AuthLayout.cshtml)
├── Corporation/                   (Index, Create, Edit, Details)
├── Section/                       (Index, Create, Edit, Details)
├── TaskType/                      (Index, Create, Edit, Details)
├── User/                          (Index, Create, Edit)
├── Task/                          (Index, Create, Edit, Details — admin; MyTasks, Work — employee)
├── Notification/Index.cshtml      (full notification history page, paged)
├── Home/Index.cshtml, Privacy.cshtml
└── Shared/
    ├── _Layout.cshtml             (Main Skote admin layout: sidebar + nav + content + footer)
    ├── _AuthLayout.cshtml         (Login/register page layout — no sidebar)
    ├── _Sidebar.cshtml            (Left nav — Dashboard + Notifications + Tasks + My Tasks menus gated by <permission> TagHelper)
    ├── _CommentsChat.cshtml       (Shared chat panel — used by Task/Details + Task/Work; AJAX to AddComment)
    ├── _Nav.cshtml                (Top navbar — includes live notifications bell dropdown)
    ├── _Head.cshtml               (CSS includes)
    ├── _Scripts.cshtml            (JS includes — incl. SignalR client CDN + notifications.js)
    ├── _Footer.cshtml
    ├── _Notifications.cshtml      (TempData → Toastr notifications)
    ├── _RightSideBar.cshtml       (Skote theme settings sidebar)
    └── _ValidationScriptsPartial.cshtml
```

> Task Create/Edit use **Select2** (from `wwwroot/back/assets/libs/select2`) for the tag-style employee multi-select, a dynamic-by-type form (Counter target / Point checklist editor), and cascading Corporation→Section dropdowns.

---

## Authorization System

### Roles (defined in `Tasks.Domain/Authorization/Roles.cs`)
- `Roles.Admin` = `"Admin"` — task creators; full access
- `Roles.Employee` = `"Employee"` — assignees; limited access

### Permissions (defined in `Tasks.Domain/Authorization/Permissions.cs`)
Grouped by feature. Claim type: `Permissions.ClaimType = "permission"`.

| Permission Constant | Value | Admin | Employee |
|---|---|:---:|:---:|
| `Permissions.Corporations.Manage` | `Corporations.Manage` | ✓ | |
| `Permissions.Sections.Manage` | `Sections.Manage` | ✓ | |
| `Permissions.TaskTypes.Manage` | `TaskTypes.Manage` | ✓ | |
| `Permissions.Users.Manage` | `Users.Manage` | ✓ | |
| `Permissions.Tasks.Create` | `Tasks.Create` | ✓ | |
| `Permissions.Tasks.ViewAll` | `Tasks.ViewAll` | ✓ | |
| `Permissions.Tasks.ViewAssigned` | `Tasks.ViewAssigned` | | ✓ |
| `Permissions.Tasks.Comment` | `Tasks.Comment` | ✓ | ✓ |
| `Permissions.Tasks.UpdateProgress` | `Tasks.UpdateProgress` | | ✓ |

### How it works (end-to-end)
1. **Seed:** `AppIdentityDbContextSeed` creates `Admin` and `Employee` roles, then assigns users.
2. **Cookie:** `AppUserClaimsPrincipalFactory` fires at login — expands the user's role into individual `permission` claims stored in the auth cookie. No per-request DB hit.
3. **Server guard:** `[Authorize(Policy = Permissions.Corporations.Manage)]` — `PermissionPolicyProvider` auto-creates a policy for any permission constant; `PermissionAuthorizationHandler` checks `HasClaim("permission", ...)`.
4. **Client guard:** `<permission required="@Permissions.Corporations.Manage">…</permission>` — `PermissionTagHelper` suppresses output when the user lacks the claim. Used in `_Sidebar.cshtml` and action-button areas. Server and client guards are driven by the same claims, so they can never drift.
5. **Admin → not assignable to tasks:** Domain rule (not a permission) — the user picker for task assignment queries only the `Employee` role via `UserManager.GetUsersInRoleAsync(Roles.Employee)`, so admins never appear. POST re-validates.

### Adding a new permission
1. Add a constant to `Permissions.cs`.
2. Add it to `RolePermissions._map` for the relevant role(s).
3. Decorate the controller/action: `[Authorize(Policy = Permissions.NewFeature.Action)]`.
4. Gate the sidebar/button: `<permission required="@Permissions.NewFeature.Action">`.
No policy registration needed — `PermissionPolicyProvider` auto-wires it.

---

## ViewModels

- **`LoginViewModel`** — `EmailOrUserName` (required), `Password` (required), `RememberMe`
- **`CorporationViewModel`** — `Id`, `Code?` (read-only), `Name` (required, 2-200 chars, `[Remote]` unique check), `NameAr?` (max 200), `Notes?` (max 5000)
- **`SectionViewModel`** — `Id`, `Code?`, `Name`, `CorporationId`, `Email?`, `Fax?`, `Phone?`, `Address?`, `Telex?`, `Notes?`, `SelectedUserIds`, display helpers (`CorporationName`, `MemberCount`, `Corporations`, `AvailableEmployees`)
- **`TaskTypeViewModel`** — `Id`, `Name` (required, unique), `Category` (TaskCategory enum)
- **`UserViewModel`** — `Id?`, `FirstName`, `LastName`, `UserName`, `Email`, `PhoneNumber?`, `Gender`, `Role` (required), `CorporationId?`, `SectionId?`, `IsActive`, `Password?`/`ConfirmPassword?` (create-only / optional on edit), display helpers (`CorporationName`, `SectionName`, `Corporations`, `Sections`, `Roles`)
- **`AvailableEmployeeViewModel`** — `Id`, `FullName`, `Email`, `IsSelected` — used in Section + Task create/edit employee pickers
- **`WorkTaskViewModel`** — `Id`, `Code?`, `Title`, `Description?`, `Notes?`, `RequestDate`, `DueDate`, `Priority`, `TaskTypeId`, `CorporationId`, `SectionId?`, `TargetCount?`, `SelectedUserIds`, `Points` (List<TaskPointViewModel>), display helpers (`Status`, `TaskTypeName`, `TaskCategory`, `CorporationName`, `SectionName`, `CreatedByName`, `AssigneeCount`), select lists (`TaskTypes`, `Corporations`, `Sections`, `AvailableEmployees`), `TaskTypeCategoryMap` (TaskTypeId→category int, drives the dynamic form)
- **`TaskPointViewModel`** — `Id`, `Description` (required), `Order`
- **`TaskCommentViewModel`** — `Id`, `Content?`, `FileUrl?`, `Type` (CommentType), `CreatedAt`, `UserId`, `UserName`, `UserImageUrl?`, `IsMine`
- **`CommentsPanelViewModel`** — `WorkTaskId`, `Comments` — model for the shared `_CommentsChat` partial
- **`MyTaskViewModel`** — slim employee-list row: `Id`, `Code`, `Title`, `Category`, `Priority`, `DueDate`, `MyStatus`, `CorporationName`, `ProgressPercent`
- **`TaskWorkViewModel`** — employee work view: task header + `AssignmentId`, `MyStatus`, `TargetCount?`/`CompletedCount?`, `Points` (List<TaskPointWorkViewModel> with `PointStatusId`/`Order`/`Description`/`IsCompleted`), `ProgressPercent`, `Comments`
- **`AssignmentProgressViewModel`** — per-assignee row on admin Details: `UserName`, `Status`, `ProgressPercent`, `CompletedCount?`, `TargetCount?`, `PointsDone`, `PointsTotal` (concrete type — anonymous types can't cross the runtime-compiled view assembly via ViewBag)
- **`NotificationViewModel`** — `Id`, `Type`, `Title`, `Message`, `Url?`, `IsRead`, `CreatedAt`, actor display (`ActorName?`, `ActorImageUrl?`, `ActorGender`), computed helpers (`AvatarSrc`, `Icon`/`ColorClass` by Type, `TimeAgo`)
- **`ErrorViewModel`** — `RequestId`

---

## File Storage

- **`IFileStorageService`** (`Tasks.Domain/Services/`) → **`FileStorageService`** (`Tasks.Repository/Storage/`) — disk-backed file upload/delete abstraction. `SaveFileAsync(Stream, originalFileName, folder)` writes to `{basePath}/Files/{folder}` with a GUID-prefixed name and returns it (auto-creates the folder); `DeleteFile(fileName, folder)`; `IsImage(fileName)` — the **single source of truth for image-type checks** (extension-based): it both **classifies** a comment file (Image vs File in `TaskController.AddComment`) and **validates** profile-picture uploads (`UserController.Create`/`Edit`, `AccountController.UpdateImage` reject non-images via `_fileStorage.IsImage(file.FileName)`). **Takes a `Stream`, not `IFormFile`** — so the Domain/Repository stay web-agnostic and any host (MVC now, a future `Tasks.Api`) can reuse it. Controllers convert `IFormFile → OpenReadStream()` at the call site. `basePath` is injected at the composition root (`builder.Environment.WebRootPath`), so it isn't hardcoded to `wwwroot`. Registered `AddScoped<IFileStorageService>(_ => new FileStorageService(builder.Environment.WebRootPath))`.

---

## File Structure Reference

```
Pixel.Tasks/
├── Pixel.Tasks.sln
├── CLAUDE.md
├── Tasks.Domain/
│   ├── Tasks.Domain.csproj
│   ├── IUnitOfWork.cs
│   ├── Enums/
│   │   ├── Gender.cs
│   │   ├── TaskCategory.cs
│   │   ├── PriorityLevel.cs
│   │   ├── WorkTaskStatus.cs
│   │   ├── CommentType.cs
│   │   └── NotificationType.cs
│   ├── Models/
│   │   ├── BaseModel.cs
│   │   ├── ICodedEntity.cs
│   │   ├── Corporation.cs
│   │   ├── Section.cs
│   │   ├── TaskType.cs
│   │   ├── WorkTask.cs
│   │   ├── TaskAssignment.cs
│   │   ├── TaskPoint.cs
│   │   ├── TaskPointStatus.cs
│   │   ├── TaskComment.cs
│   │   ├── Notification.cs
│   │   └── Identity/
│   │       └── AppUser.cs
│   ├── Repositories/
│   │   └── IGenericRepository.cs
│   ├── Services/                        (interfaces only)
│   │   ├── ICodeGeneratorService.cs
│   │   ├── INotificationService.cs
│   │   ├── IRealtimeNotificationPublisher.cs
│   │   ├── IWorkTaskService.cs
│   │   ├── IFileStorageService.cs
│   │   └── Contracts/
│   │       ├── TaskPointInput.cs
│   │       └── ProgressResult.cs
│   ├── Rules/
│   │   └── WorkTaskStatusCalculator.cs
│   ├── Authorization/
│   │   ├── Roles.cs
│   │   ├── Permissions.cs
│   │   └── RolePermissions.cs
│   └── Specifications/
│       ├── BaseSpecifications.cs
│       ├── ISpecifications.cs
│       ├── CorporationSpec/
│       │   ├── CorporationSpec.cs
│       │   └── CorporationByNameSpec.cs
│       ├── SectionSpec/
│       │   ├── SectionSpec.cs
│       │   ├── SectionByNameSpec.cs
│       │   └── SectionByCorporationSpec.cs
│       ├── TaskTypeSpec/
│       │   ├── TaskTypeSpec.cs
│       │   └── TaskTypeByNameSpec.cs
│       ├── WorkTaskSpec/
│       │   ├── WorkTaskSpec.cs
│       │   └── WorkTaskByUserSpec.cs
│       ├── TaskAssignmentSpec/
│       │   └── TaskAssignmentSpec.cs
│       ├── TaskCommentSpec/
│       │   └── TaskCommentSpec.cs
│       └── NotificationSpec/
│           ├── NotificationByUserSpec.cs
│           ├── UnreadNotificationByUserSpec.cs
│           ├── NotificationByIdSpec.cs
│           └── NotificationByWorkTaskSpec.cs
├── Tasks.Repository/
│   ├── Tasks.Repository.csproj
│   ├── GenericRepository.cs
│   ├── SpecificationEvalutor.cs
│   ├── UnitOfWork.cs
│   ├── Storage/
│   │   └── FileStorageService.cs
│   └── Data/
│       ├── TaskContext.cs
│       ├── AppIdentityDbContextSeed.cs
│       ├── Configrations/
│       │   ├── AppUserConfig.cs
│       │   ├── CorporationConfig.cs
│       │   ├── SectionConfig.cs
│       │   ├── TaskTypeConfig.cs
│       │   ├── WorkTaskConfig.cs
│       │   ├── TaskAssignmentConfig.cs
│       │   ├── TaskPointConfig.cs
│       │   ├── TaskPointStatusConfig.cs
│       │   ├── TaskCommentConfig.cs
│       │   └── NotificationConfig.cs
│       ├── DataSeed/  (empty — seed is in AppIdentityDbContextSeed.cs)
│       └── Migrations/
├── Tasks.Services/
│   ├── Tasks.Services.csproj
│   ├── CodeGeneration/
│   │   └── CodeGeneratorService.cs
│   ├── Notifications/
│   │   └── NotificationService.cs
│   └── WorkTasks/
│       └── WorkTaskService.cs
└── Tasks.Presentation/
    ├── Tasks.Presentation.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── Authorization/
    │   ├── PermissionRequirement.cs
    │   ├── PermissionAuthorizationHandler.cs
    │   ├── PermissionPolicyProvider.cs
    │   └── AppUserClaimsPrincipalFactory.cs
    ├── TagHelpers/
    │   └── PermissionTagHelper.cs
    ├── Hubs/
    │   └── NotificationHub.cs
    ├── Services/
    │   └── SignalRNotificationPublisher.cs
    ├── Controllers/
    │   ├── AccountController.cs
    │   ├── HomeController.cs
    │   ├── CorporationController.cs
    │   ├── SectionController.cs
    │   ├── TaskTypeController.cs
    │   ├── UserController.cs
    │   ├── TaskController.cs
    │   └── NotificationController.cs
    ├── ViewModels/
    │   ├── LoginViewModel.cs
    │   ├── CorporationViewModel.cs
    │   ├── SectionViewModel.cs
    │   ├── TaskTypeViewModel.cs
    │   ├── UserViewModel.cs
    │   ├── AvailableEmployeeViewModel.cs
    │   ├── WorkTaskViewModel.cs
    │   ├── TaskPointViewModel.cs
    │   ├── TaskCommentViewModel.cs
    │   ├── CommentsPanelViewModel.cs
    │   ├── MyTaskViewModel.cs
    │   ├── TaskWorkViewModel.cs
    │   ├── AssignmentProgressViewModel.cs
    │   ├── NotificationViewModel.cs
    │   └── ErrorViewModel.cs
    ├── MappingProfiles/
    │   ├── CorporationProfile.cs
    │   ├── SectionProfile.cs
    │   ├── TaskTypeProfile.cs
    │   ├── WorkTaskProfile.cs
    │   └── NotificationProfile.cs
    ├── Factories/
    │   ├── IWorkTaskViewModelFactory.cs
    │   ├── WorkTaskViewModelFactory.cs
    │   ├── IDashboardViewModelFactory.cs
    │   └── DashboardViewModelFactory.cs
    ├── Validation/
    │   └── WorkTaskValidator.cs
    ├── Helpers/
    │   ├── AvatarHelper.cs
    │   ├── NotificationDisplay.cs
    │   ├── TaskStatusBadge.cs
    │   ├── TaskPriorityBadge.cs
    │   ├── CommentTypeDisplay.cs
    │   └── DeadlineBadge.cs
    ├── Views/
    │   ├── _ViewImports.cshtml
    │   ├── _ViewStart.cshtml
    │   ├── Account/Login.cshtml, AccessDenied.cshtml
    │   ├── Home/Index.cshtml, Privacy.cshtml
    │   ├── Corporation/Index.cshtml, Create.cshtml, Edit.cshtml, Details.cshtml
    │   ├── Section/Index.cshtml, Create.cshtml, Edit.cshtml, Details.cshtml
    │   ├── TaskType/Index.cshtml, Create.cshtml, Edit.cshtml, Details.cshtml
    │   ├── User/Index.cshtml, Create.cshtml, Edit.cshtml
    │   ├── Task/Index.cshtml, Create.cshtml, Edit.cshtml, Details.cshtml, MyTasks.cshtml, Work.cshtml
    │   ├── Notification/Index.cshtml
    │   └── Shared/ (_Layout, _AuthLayout, _Sidebar, _CommentsChat, _Nav, _Head, _Scripts, _Footer, _Notifications, _RightSideBar, _ValidationScriptsPartial, Error)
    ├── Logs/
    └── wwwroot/ (Skote template: css/, js/, lib/, back/, favicon.ico)
```

---

## Coding Conventions

1. **PascalCase** for public properties, class names, method names, namespace parts
2. **camelCase** for private fields (e.g., `private readonly TaskContext dbContext;`)
3. **_camelCase** for injected dependencies in services/controllers (e.g., `_unitOfWork`, `_mapper`)
4. **Spec suffix** for specification classes (e.g., `CorporationSpec`)
5. **SpecParams suffix** for specification parameter classes (e.g., `ProductSpecParams`)
6. **ViewModel suffix** for MVC view models (e.g., `CorporationViewModel`)
7. **Config suffix** for EF configurations (e.g., `CorporationConfig`)
8. **Profile suffix** for AutoMapper profiles (e.g., `CorporationProfile`)
9. Service interfaces defined in `Tasks.Domain/Services/`, implementations in `Tasks.Services/`
10. Repository interface in `Tasks.Domain/Repositories/`, implementation in `Tasks.Repository/`
11. Enums in `Tasks.Domain/Enums/`
12. `DateTime.UtcNow` used everywhere for timestamps
13. All entities that are DB-persisted inherit from `BaseModel`
14. String-based includes (`IncludeStrings`) used for multi-level navigation (ThenInclude equivalent)
15. Comments only when intent is not obvious; remove pointless or redundant comments
16. Controllers use `[Authorize(Policy = Permissions.X.Y)]` for permission-based access; `[Authorize]` for authenticated-only (dashboard); login/access-denied actions use `[AllowAnonymous]`
17. Delete actions return JSON for AJAX calls; Create/Edit use POST + redirect pattern
18. `[Remote]` attribute on ViewModels for client-side uniqueness validation
19. Authorization constants (`Roles`, `Permissions`) live in `Tasks.Domain/Authorization/` — no web/infra deps

---

## UI/UX Guidelines

- **Template:** Skote Admin & Dashboard (Bootstrap 5)
- **Client preference:** Modern, professional, premium-feeling UI/UX
- **Sidebar:** Dark mode (`data-sidebar="dark"`), Boxicons for menu icons
- **Notifications:** Toastr for success/error/warning messages via `TempData`
- **Tables:** DataTables plugin for server-side CRUD listing pages
- **Forms:** Bootstrap card-based forms with client-side validation (`jquery.validate` + `jquery.validate.unobtrusive`)
- **Delete:** SweetAlert2 confirmation → AJAX POST → Toastr notification
- **Auth layout:** `_AuthLayout.cshtml` (separate from admin layout, no sidebar)
- **Admin layout:** `_Layout.cshtml` (sidebar + topnav + footer + right sidebar theme settings)

---

## Important Notes

- **`SpecificationEvalutor.cs`** filename has a typo (missing 'a' in Evaluator) — this is the established convention, do NOT rename it
- **`Data/Configrations/`** folder has a typo — this is the established convention, do NOT rename it
- **`WorkTask`** is the entity name for "Task" — avoids collision with `System.Threading.Tasks.Task`
- **No roles/permissions are implemented yet** — RBAC (Roles + Permissions) is planned for later
- **No API endpoints** — this is a server-rendered MVC app (Razor Views), NOT a Web API
- **Seed data does NOT assign roles** — only creates users
- **File upload** is now `IFileStorageService` (Domain) → `FileStorageService` (Tasks.Repository). The old `DocumentSettings.UplaodFile` static helper (with its typo) has been removed; the new method is correctly spelled `SaveFileAsync` and is `Stream`-based so a future `Tasks.Api` can reuse it without referencing the web project

---

## Gotchas & Past Mistakes for Claude

> **This section tracks mistakes Claude has made on this project so they are never repeated.**
> Add entries here whenever Claude makes an error during development.

### 2026-06-27 — SQL Server cascade delete cycles
**What went wrong:** All FKs were set to `Cascade` delete, causing SQL Server error 1785 (multiple cascade paths) when running migrations.  
**Root cause:** Corporation → Section → WorkTask AND Corporation → WorkTask (direct) create multiple cascade paths to the same table. SQL Server forbids this.  
**Correct approach:** Use `DeleteBehavior.NoAction` for "reference" FKs (AppUser→Corp/Section, WorkTask→Corp/Section/CreatedBy, etc.) and keep `Cascade` only for true "ownership" FKs (WorkTask→Assignments/Points/Comments). See FK Cascade Strategy table above.

### 2026-06-27 — EF Core HasDefaultValue type mismatch for enums
**What went wrong:** Used `.HasDefaultValue(1)` (int) for a property typed as `WorkTaskStatus` (enum), causing EF Core design-time error: "Cannot set default value '1' of type 'int' on property of type 'WorkTaskStatus'".  
**Root cause:** EF Core enforces exact type matching between the default value and the property type.  
**Correct approach:** Remove `HasDefaultValue()` entirely — the C# model already initializes `Status = WorkTaskStatus.Pending`, so EF always inserts the correct value without a DB-level default.

### 2026-06-27 — Corporation.cs edit not persisting (CRLF mismatch)
**What went wrong:** `replace_file_content` silently failed to match content due to CRLF vs LF line ending mismatch in `Corporation.cs`.  
**Root cause:** The file had mixed line endings; the replacement target string used LF but the file had CRLF.  
**Correct approach:** When a targeted replace fails silently, verify the file content with `view_file` and use `write_to_file` with `Overwrite=true` as a fallback.

### 2026-06-28 — DocumentSettings.UplaodFile throws DirectoryNotFoundException
**What went wrong:** Uploading a file as a comment threw `System.IO.DirectoryNotFoundException` because `wwwroot/Files/TaskComments` did not exist on disk.  
**Root cause:** `DocumentSettings.UplaodFile` constructs the folder path and immediately opens a `FileStream` without ever creating the directory first.  
**Correct approach:** Call `Directory.CreateDirectory(FolderPath)` immediately after building `FolderPath` and before opening the stream — it is a no-op if the folder already exists, so it is always safe to include. This must be done for every new upload folder, not just `TaskComments`.

### 2026-06-29 — Submit button stuck on "Saving…" when client validation fails
**What went wrong:** On Task Create/Edit, clicking Save with a client-side validation error (e.g. Task Type required) left the button permanently disabled showing the "Saving…" spinner, so the user could not retry.  
**Root cause:** The form's `submit` handler disabled the button and swapped in the spinner *unconditionally*, before jQuery unobtrusive validation ran. When validation then blocked the actual submit, the button stayed stuck.  
**Correct approach:** Gate the button-disable on validity — `if (!$(this).valid()) return;` (the form is `novalidate`, so call `$(form).valid()` explicitly) *before* disabling the button and showing the spinner. For Edit's SweetAlert confirm path, place the check before the dialog and skip it on the programmatic re-submit (guard with the `confirmed` flag) so a valid form isn't re-validated. All other CRUD forms (User, TaskType, Corporation, Section) already follow this pattern — match it for any new form with a submit spinner.

<!-- 
FORMAT FOR NEW ENTRIES:
### [Date] — Short description
**What went wrong:** Describe the mistake.
**Root cause:** Why it happened.
**Correct approach:** What should have been done instead.
-->
