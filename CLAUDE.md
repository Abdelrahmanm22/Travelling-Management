# CLAUDE.md — Travel Management SaaS

## Project Overview

A **.NET 9 MVC Web Application** (server-rendered Razor Views) — a **SaaS for travel companies** to manage their **internal operations/processes**.



**Solution:** `Travelling management.sln` · **Framework:** `net9.0` · **DB:** SQL Server (`TravelDB`) via EF Core 9
**Auth:** ASP.NET Identity (cookie-based, MVC — NOT JWT) · **Logging:** Serilog (console + daily rolling files in `Travel.Presentation/Logs/`) · **Mapping:** AutoMapper (registration commented out until first profile) · **UI:** Skote Admin Template (Bootstrap 5, dark sidebar, Boxicons)
**UI Preference:** Client wants a modern/professional/premium UI/UX — use modern elements and creative touches.

---

## Architecture — Onion / Clean Architecture

```
Travel.Domain      (Core — zero project references)
    ├── Models/            (BaseModel; Models/Identity/AppUser)
    ├── Enums/             (Gender)
    ├── Repositories/      (IGenericRepository<T>)
    ├── Services/          (interfaces only — IFileStorageService)
    ├── Specifications/    (ISpecifications<T>, BaseSpecifications<T>)
    └── IUnitOfWork.cs

Travel.Repository  (Infrastructure — references Travel.Domain)
    ├── Data/TravelContext.cs              (IdentityDbContext<AppUser>)
    ├── Data/Configrations/                (Fluent API per entity — "Configrations" typo is established, keep it)
    ├── Data/AppIdentityDbContextSeed.cs   (seeds the first admin user)
    ├── Data/Migrations/                   (InitialMigration)
    ├── GenericRepository.cs
    ├── SpecificationEvalutor.cs           (typo intentional — keep it)
    ├── Storage/FileStorageService.cs      (IFileStorageService impl — disk-backed)
    └── UnitOfWork.cs

Travel.Services    (Business Logic — references Travel.Domain) — empty; service impls land here

Travel.Presentation (MVC Web App — references Travel.Repository + Travel.Services)
    ├── Controllers/       (AccountController, HomeController)
    ├── ViewModels/        (LoginViewModel; ErrorViewModel [namespace Travel.Presentation.Models])
    ├── Views/             (Account/Login, Home/Index+Privacy, Shared layout partials)
    ├── Helpers/, MappingProfiles/ (empty — reserved)
    ├── wwwroot/           (Skote assets: css, js, lib, back/)
    ├── Program.cs         (startup, DI, pipeline)
    └── appsettings.json
```

### Dependency Flow

```
Travel.Presentation → Travel.Services   → Travel.Domain
Travel.Presentation → Travel.Repository → Travel.Domain
```

**Key rule:** `Travel.Domain` has ZERO project references. Its only NuGet is `Microsoft.AspNetCore.Identity.EntityFrameworkCore`. Service **interfaces** live in `Travel.Domain/Services/`; **implementations** in `Travel.Services/` (or `Travel.Presentation` for web-coupled ones).

---

## NuGet Packages by Project

| Project | Key Packages |
|---|---|
| **Travel.Domain** | `Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.18` |
| **Travel.Repository** | `Microsoft.EntityFrameworkCore.SqlServer 9.0.18` (+ ref Travel.Domain) |
| **Travel.Services** | _(no NuGet — only ref Travel.Domain)_ |
| **Travel.Presentation** | `AutoMapper.Extensions.Microsoft.DependencyInjection 12.0.1`, `Serilog.AspNetCore 10.0.0`, `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation 9.0.18`, `Microsoft.EntityFrameworkCore.Tools 9.0.18`, `Microsoft.VisualStudio.Web.CodeGeneration.Design 9.0.12` |

---

## Domain Models

- **`BaseModel`** — `int Id`. All DB-persisted entities inherit from it.
- **`AppUser`** : `IdentityUser` — `FirstName`, `LastName`, `FullName` (computed), `Gender` (enum), `IsActive` (default true), `ImageUrl?`. Configured in `AppUserConfig` (FirstName/LastName required max 100; ImageUrl max 500; unique index on UserName + Email).

> Travel-domain entities  **not yet modeled** — add them here as the domain is designed. A future coded-entity convention (`ICodedEntity` + a code-generator service) can be reintroduced if sequential codes are needed.

---

## Enums

| Enum | Values | Location |
|---|---|---|
| `Gender` | `Male=1`, `Female=2` | `Travel.Domain/Enums/` |

---

## Key Patterns

### Generic Repository + Specification

All data access goes through `IGenericRepository<T>` + `ISpecifications<T>`:

```csharp
var spec = new SomeSpecification(params);
var one  = await _unitOfWork.Repository<Entity>().GetByIdAsync(spec);
var many = await _unitOfWork.Repository<Entity>().GetAllAsync(spec);
```

**`IGenericRepository<T>`:** `GetAllAsync(spec)`, `GetByIdAsync(spec)`, `AddAsync(entity)`, `Update(entity)`, `Delete(entity)`.
**`ISpecifications<T>`:** `Criteria` (WHERE), `Includes` (eager-load expressions), `IncludeStrings` (ThenInclude chains, e.g. `"Categories.Items"`), `OrderBy`/`OrderByDesc`, `Skip`/`Take`/`IsPaginationEnabled`.
**`BaseSpecifications<T>`:** parameterless (get all) + criteria ctor; helpers `AddInclude(string)`, `SetOrderBy(...)`, `SetOrderByDesc(...)`, `ApplyPagination(skip, take)`. Name spec classes with the **`Spec`** suffix in per-entity folders under `Travel.Domain/Specifications/`.

### Unit of Work + Transactions

`_unitOfWork.Repository<Entity>()` (cached per entity in a `Hashtable`) · `await _unitOfWork.CompleteAsync()` (SaveChanges).

Wrap **multiple** DB operations that must be atomic in an explicit transaction:

```csharp
await _unitOfWork.BeginTransactionAsync();
try { /* ... multiple Repository + CompleteAsync calls ... */ await _unitOfWork.CommitTransactionAsync(); }
catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
```

Use a transaction when a method calls `CompleteAsync()` more than once (e.g. needs a DB-generated Id mid-flow) or writes two+ unrelated entities atomically. A single `CompleteAsync()` is already atomic — no transaction needed. `DisposeAsync` rolls back any open transaction to prevent connection leaks.

### SpecificationEvalutor

`Travel.Repository/SpecificationEvalutor.cs` (typo intentional) builds the EF query: Criteria → OrderBy → OrderByDesc → Pagination → Includes → IncludeStrings.

---

## Service Layer

| Interface (Travel.Domain/Services) | Implementation | Purpose |
|---|---|---|
| `IFileStorageService` | `FileStorageService` (Travel.Repository/Storage) | Disk-backed upload/delete — see File Storage |

> `Travel.Services` is empty. Business-logic services go there (interface in Domain, impl in Services). Keep them free of web types (`IFormFile`, `UserManager`, controllers) so a future `Travel.Api` could reuse them.

---

## Database

**`TravelContext` : `IdentityDbContext<AppUser>`** — applies configs via `modelBuilder.ApplyConfigurationsFromAssembly(...)`. Only `AspNet*` Identity tables exist so far (with `AppUser`'s custom columns). Add DbSets + `Config` classes as entities are introduced.

**FK cascade note (SQL Server):** SQL Server forbids multiple cascade paths to the same table (error 1785). As relations are added, use `DeleteBehavior.Cascade` only for true ownership FKs and `NoAction` for reference FKs to break cycles.

**Seeding:** `AppIdentityDbContextSeed.SeedUserAsync` seeds one admin (`MustafaAdmin`) on first run, called from `Program.cs` after `MigrateAsync()`. No roles seeded yet.

**Connection string** (`appsettings.json` → `DefaultConnection`):
```
Server=.;Database=TravelDB;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;
```

---

## Program.cs — DI Registration & Pipeline

```csharp
// Data & infra
AddDbContext<TravelContext>(SqlServer)
AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>))
AddScoped<IUnitOfWork, UnitOfWork>()
AddScoped<IFileStorageService>(_ => new FileStorageService(builder.Environment.WebRootPath))

// MVC
AddControllersWithViews() + AddRazorRuntimeCompilation()
// AutoMapper — registration commented out; enable when the first profile exists

// Identity (cookie auth)
AddIdentity<AppUser, IdentityRole>(password policy)
    .AddEntityFrameworkStores<TravelContext>()   // REQUIRED — wires the user/role stores
    .AddDefaultTokenProviders()
ConfigureApplicationCookie: LoginPath=/Account/Login, AccessDeniedPath=/Account/AccessDenied, 8h sliding
```

**Startup:** `MigrateAsync()` then seed, inside a try/catch that logs via Serilog.
**Pipeline:** `ExceptionHandler`/`HSTS` (prod) → HTTPS → Routing → Authentication → Authorization → StaticAssets → MapControllerRoute. Default route `{controller=Account}/{action=Login}/{id?}` — app starts on the Login page.

---

## Controllers & Views

### AccountController
| Action | Method | Auth | Description |
|---|---|---|---|
| `Login` | GET/POST | Anonymous | Cookie sign-in; supports email OR username; blocks inactive users; honors local `returnUrl` |
| `SignOut` | GET | Authorize | Sign out → Login |
| `AccessDenied` | GET | Anonymous | 403 page |

> A profile view/update-image flow is stubbed out (commented) in `AccountController` — reactivate it once the profile view + `ProfileViewModel` exist.

### HomeController (`[Authorize]` to be added)
`Index` (dashboard placeholder), `Privacy`, `Error`.

### Views
`Account/Login`, `Home/Index`+`Privacy`, and `Shared/` layout partials (`_Layout`, `_AuthLayout`, `_Sidebar`, `_Nav`, `_Head`, `_Scripts`, `_Footer`, `_RightSideBar`, `_ValidationScriptsPartial`, `Error`). `_ViewImports` imports `Travel.Presentation` + `Travel.Presentation.Models`.

---

## Authorization System (planned)

Not implemented yet. When added, follow the claims-based pattern: `Roles`/`Permissions` constants in `Travel.Domain/Authorization/`; a `permission` claim expanded at login via a custom `ClaimsPrincipalFactory`; `[Authorize(Policy = ...)]` on actions with an auto policy provider; and a `<permission>` TagHelper to gate UI. Server + client guards must share the same claims so they can't drift.

---

## ViewModels

- **`LoginViewModel`** — `EmailOrUserName` (required), `Password` (required), `RememberMe`.
- **`ErrorViewModel`** — `RequestId` (namespace `Travel.Presentation.Models`, not `.ViewModels`).

Name new ones with the **`ViewModel`** suffix; use `[Remote]` for client-side uniqueness checks.

---

## File Storage

**`IFileStorageService`** (Travel.Domain) → **`FileStorageService`** (Travel.Repository/Storage):
- `SaveFileAsync(Stream, originalFileName, folder)` → writes to `{basePath}/Files/{folder}` with a GUID-prefixed name (auto-creates the folder), returns the stored name.
- `DeleteFile(fileName, folder)` — no-op if missing.
- `IsImage(fileName)` — extension-based; **single source of truth** for image-vs-file checks (comment classification + profile-picture validation).

Takes a **`Stream`, not `IFormFile`**, so Domain/Repository stay web-agnostic; controllers pass `IFormFile.OpenReadStream()`. `basePath` is injected at startup (`builder.Environment.WebRootPath`).

---

## File Structure Reference

```
Travelling management/
├── Travelling management.sln
├── CLAUDE.md
├── .claude/commands/commit.md          (custom /commit workflow)
├── Travel.Domain/
│   ├── IUnitOfWork.cs
│   ├── Enums/Gender.cs
│   ├── Models/BaseModel.cs, Models/Identity/AppUser.cs
│   ├── Repositories/IGenericRepository.cs
│   ├── Services/IFileStorageService.cs
│   └── Specifications/BaseSpecifications.cs, ISpecifications.cs
├── Travel.Repository/
│   ├── GenericRepository.cs, SpecificationEvalutor.cs, UnitOfWork.cs
│   ├── Storage/FileStorageService.cs
│   └── Data/TravelContext.cs, AppIdentityDbContextSeed.cs,
│       Configrations/AppUserConfig.cs, Migrations/…InitialMigration
├── Travel.Services/                    (empty — business services go here)
└── Travel.Presentation/
    ├── Program.cs, appsettings.json
    ├── Controllers/AccountController.cs, HomeController.cs
    ├── ViewModels/LoginViewModel.cs, ErrorViewModel.cs
    ├── Views/ (Account, Home, Shared)
    └── wwwroot/ (Skote template: css/, js/, lib/, back/, favicon.ico)
```

---

## Coding Conventions

1. **PascalCase** public members/types; **camelCase** private fields; **_camelCase** injected deps (`_unitOfWork`, `_mapper`).
2. Suffixes: `Spec` (specifications), `SpecParams`, `ViewModel`, `Config` (EF configs), `Profile` (AutoMapper).
3. Service interfaces in `Travel.Domain/Services/`, impls in `Travel.Services/`; repo interface in Domain, impl in Repository; enums in `Travel.Domain/Enums/`.
4. `DateTime.UtcNow` for all timestamps. All DB entities inherit `BaseModel`.
5. `IncludeStrings` for multi-level (ThenInclude) navigation.
6. Comments only when intent isn't obvious — remove redundant ones.
7. Controllers: `[Authorize(Policy = ...)]` for permission gates, `[Authorize]` for authenticated-only, `[AllowAnonymous]` for login/access-denied. Delete actions return JSON for AJAX; Create/Edit use POST + redirect.
8. Keep controllers HTTP-only — push orchestration into services and view-model building into factories as the app grows.

---

## UI/UX Guidelines

- **Template:** Skote Admin & Dashboard (Bootstrap 5); dark sidebar (`data-sidebar="dark"`), Boxicons.
- **Client preference:** modern, professional, premium feel.
- **Notifications:** Toastr via `TempData`. **Tables:** DataTables. **Delete:** SweetAlert2 confirm → AJAX POST → Toastr.
- **Forms:** Bootstrap card forms with `jquery.validate` + unobtrusive validation.
- **Layouts:** `_AuthLayout` (login, no sidebar) vs `_Layout` (admin: sidebar + topnav + footer + theme sidebar).
- **Branding:** Musta Travel logo at `wwwroot/back/assets/images/Logo_MustaTravel.png` — use it in the auth + admin layouts.

---

## Important Notes

- **`SpecificationEvalutor.cs`** and **`Data/Configrations/`** have intentional typos — do NOT rename them.
- Seed assigns no roles yet; RBAC is planned.
- File upload is `IFileStorageService` (Domain) → `FileStorageService` (Repository); `Stream`-based so a future `Travel.Api` can reuse it.

---

## Gotchas & Past Mistakes for Claude

> Track every mistake here so it is never repeated.

### 2026-07-17 — Identity registered without `AddEntityFrameworkStores`
**What went wrong:** `AddIdentity<AppUser, IdentityRole>(...)` was configured but never chained to `.AddEntityFrameworkStores<TravelContext>()`, so `IUserStore` couldn't be resolved. This broke the EF **design-time** factory (`dotnet ef migrations add` failed to build the `DbContext`) **and** would crash at runtime (UserManager/SignInManager unconstructable).
**Fix:** chain `.AddEntityFrameworkStores<TravelContext>().AddDefaultTokenProviders()` onto `AddIdentity`.

### Reusable guardrails (from the same architecture — apply as the domain is built)
- **SQL Server cascade cycles (error 1785):** don't set every FK to `Cascade`. Use `Cascade` only for ownership FKs, `NoAction` for reference FKs to break multiple cascade paths to the same table.
- **EF enum default value:** don't `HasDefaultValue(1)` (int) on an enum property — type mismatch at design time. Initialize the default in the C# model instead and omit the DB default.
- **Upload directory:** create the target folder before opening a `FileStream` (`Directory.CreateDirectory` is a safe no-op if it exists). `FileStorageService` already does this — keep it for any new upload folder.
- **Submit button "Saving…" stuck:** gate the disable-button/spinner on validity — `if (!$(form).valid()) return;` *before* disabling — so a client-side validation failure doesn't permanently disable the button.
- **CRLF vs LF edits:** if a targeted string replace silently fails to match, verify with a fresh read and fall back to overwriting the whole file.
