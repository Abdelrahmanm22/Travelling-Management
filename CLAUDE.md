# CLAUDE.md — Travel Management SaaS

## Project Overview

A **.NET 9 MVC Web Application** (server-rendered Razor Views) — a **SaaS for travel companies** to manage their **internal operations/processes**.



**Solution:** `Travelling management.sln` · **Framework:** `net9.0` · **DB:** SQL Server (`TravelDB`) via EF Core 9
**Auth:** ASP.NET Identity (cookie-based, MVC — NOT JWT) · **Logging:** Serilog (console + daily rolling files in `Travel.Presentation/Logs/`) · **Mapping:** AutoMapper (registered — `AddAutoMapper(typeof(Program).Assembly)`) · **UI:** Skote Admin Template (Bootstrap 5, dark sidebar, Boxicons)
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

Travel.Services    (Business Logic — references Travel.Domain)
    ├── ItineraryGeneratorService.cs
    └── Identity/          (RoleService, UserAccountService)

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
| `IItineraryGeneratorService` | `ItineraryGeneratorService` (Travel.Services) | Builds a booking's itinerary from its package |
| `IRoleService` | `RoleService` (Travel.Services/Identity) | Role CRUD + atomic role-claim replacement |
| `IUserAccountService` | `UserAccountService` (Travel.Services/Identity) | User CRUD, roles, direct permissions, lockout guards |

Services return **`OperationResult` / `OperationResult<T>`** (`Travel.Domain/Services/OperationResult.cs`) with `From(IdentityResult)`; controllers turn that into `ModelState` errors or the `{ success, message }` JSON the AJAX flows expect.

> Business-logic services go in `Travel.Services` (interface in Domain, impl in Services). Keep them free of **web** types — `IFormFile`, `SignInManager`, controllers, `HttpContext` — so a future `Travel.Api` could reuse them. `UserManager`/`RoleManager` **are** allowed: they live in `Microsoft.Extensions.Identity.Core` (no HTTP coupling), an API would inject the same types, and `AppUser : IdentityUser` already puts Identity on Domain's public surface. `SignInManager.RefreshSignInAsync` stays in controllers.

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
AddAutoMapper(typeof(Program).Assembly)

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

### HomeController `[Authorize]`
`Index` (dashboard placeholder), `Privacy`, `Error` (**`[AllowAnonymous]`** — it's the `UseExceptionHandler` target; requiring auth there would swallow real errors behind a login redirect).

### RoleController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` (JSON) / `IsNameAvailable`, each `[Authorize(Policy = Permissions.Roles.X)]`. Permissions are edited inline on the form, so `Roles.ManagePermissions` is enforced **inside** the POST and via `<permission>` around the picker.

### UserController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` / `ToggleActive` / `ResetPassword` (all JSON for the AJAX ones) / `Permissions` GET+POST / `IsUserNameAvailable` / `IsEmailAvailable`.
The C# methods behind `/User/Permissions` are named **`DirectPermissions`/`DirectPermissionsPost`** with `[ActionName("Permissions")]` — a member called `Permissions` would shadow the `Permissions` static class and break every `[Authorize(Policy = Permissions.X)]` in the file.

### Views
`Account/Login`+`AccessDenied`, `Home/Index`+`Privacy`, `Booking/`, `Package/`, `Destination/`, `Role/`, `User/`, and `Shared/` partials (`_Layout`, `_AuthLayout`, `_Sidebar`, `_Nav`, `_Head`, `_Scripts`, `_Footer`, `_RightSideBar`, `_Notifications`, `_ValidationScriptsPartial`, **`_PermissionModules`**, `Error`).
`_ViewImports` imports `Travel.Presentation`, `.Models`, `.ViewModels`, `Travel.Domain.Authorization`, and registers `@addTagHelper *, Travel.Presentation`.
**`Shared/_PermissionModules.cshtml`** is the one permission picker, shared by `Role/_RoleForm` and `User/Permissions` — driven by `IsLocked`, so the two can't drift. Its JS is `wwwroot/back/assets/js/role-permissions.js`.

---

## Authorization System (implemented)

Claims-based permissions on top of ASP.NET Identity. **No `Permission` table and no EF migration** — the catalog is C# constants; assignments are rows in `AspNetRoleClaims` / `AspNetUserClaims` with `ClaimType = "permission"`.

### `Travel.Domain/Authorization/`

| File | Purpose |
|---|---|
| `AppClaimTypes.cs` | `Permission = "permission"` — the claim type |
| `Permissions.cs` | Nested static classes per module; `const string` values shaped `Module.Action`. **Consts are mandatory** — `[Authorize(Policy = ...)]` needs a compile-time constant |
| `AppRoles.cs` | `SuperAdmin` const + `IsProtected(name)`. Named `AppRoles`, not `Roles`, to avoid clashing with the `Permissions.Roles` module |
| `PermissionCatalog.cs` | Explicit descriptor list (display name, description, module, order) driving the picker UI. `Sanitize()`, `IsValid()`, `FindCatalogGaps()` |

Modules: **Bookings** (+`Regenerate`), **Packages**, **Destinations**, **Users** (+`ToggleActive`, `ResetPassword`, `AssignRoles`, `ManagePermissions`), **Roles** (+`ManagePermissions`).

**Adding a permission:** add the `const` to `Permissions.cs` **and** an entry to `PermissionCatalog.Modules`. `FindCatalogGaps()` logs a Serilog warning at startup if the two drift.

### `Travel.Presentation/Authorization/`

- `PermissionRequirement` / `PermissionAuthorizationHandler` — **SuperAdmin short-circuits**; everyone else needs the matching claim.
- `PermissionPolicyProvider` (**singleton**) — builds a policy on demand when `PermissionCatalog.IsValid(policyName)`; anything else falls through to the default provider, so a typo throws loudly instead of silently 403ing.
- `AppUserClaimsPrincipalFactory` — the base `UserClaimsPrincipalFactory<AppUser, IdentityRole>` **already** unions role claims + user claims, so this only de-duplicates, drops claims for permissions removed from the catalog, and emits **none** for SuperAdmin (keeps the cookie small).

### Effective permissions

`effective = union(role permissions, direct user permissions)` — **grants only, no deny**. On `/User/Permissions/{id}`, role-inherited items render `checked disabled` (browsers never submit disabled inputs, so they can't be saved as direct claims); `ReplaceDirectPermissionsAsync` also strips them server-side, while preserving a pre-existing direct claim that a role now happens to cover.

### Propagation to signed-in users

`SecurityStampValidatorOptions.ValidationInterval = 2 min`. On tick with a matching stamp, Identity calls `RefreshSignInAsync` → re-runs the claims factory → **permissions refresh silently, no re-login**. Rotating the stamp (`UpdateSecurityStampAsync`) instead signs the user out — done on role changes, deactivation, and (by Identity) password resets.

### Gating the UI

`<permission required="@Permissions.Bookings.View" mode="any|all">…</permission>` — evaluates via `IAuthorizationService`, **not** `User.HasClaim` (a SuperAdmin has no permission claims). `output.TagName = null` erases the wrapper so `<li>` stays a direct child of `<ul class="metismenu">`. Registered by `@addTagHelper *, Travel.Presentation` in `_ViewImports`. **It's C#, so a `.cshtml`-only edit isn't enough — rebuild**, or the tag renders literally.

### Lockout guards

`UserAccountService` blocks: deleting/deactivating yourself, removing your own SuperAdmin role, and anything leaving zero active SuperAdmins. `RoleService` blocks editing/renaming/deleting SuperAdmin, and deleting any role that still has members.

### Seeding

`AppIdentityDbContextSeed.SeedRolesAsync` (creates SuperAdmin, **no claims**) → existing `SeedUserAsync` → `AssignSuperAdminAsync` (assigns SuperAdmin if nobody holds it, falling back to the oldest user; backfills the seed user's name/gender). Each is guarded independently of `if (!userManager.Users.Any())`, which is already false on any existing DB.

---

## ViewModels

- **`LoginViewModel`** — `EmailOrUserName` (required), `Password` (required), `RememberMe`.
- **`ErrorViewModel`** — `RequestId` (namespace `Travel.Presentation.Models`, not `.ViewModels`).
- **`DestinationViewModel`**, **`PackageViewModel`**, **`BookingViewModel`** (+ nested inputs).
- **`RoleViewModel`** + `PermissionGroupViewModel` / `PermissionItemViewModel` (the item carries `IsSelected`, `IsLocked`, `LockedReason`, `IsAlsoDirect` so the same partial serves roles and users).
- **`UserFormViewModel`**, `RoleOptionViewModel`, `UserPermissionsViewModel`, `ResetPasswordViewModel`.

Name new ones with the **`ViewModel`** suffix; use `[Remote]` for client-side uniqueness checks.

**Checkbox lists** (`SelectedPermissions`, `SelectedRoles`, `SelectedDepartureDays`) bind from repeated `name="X"` inputs with distinct `value`s into a `List<T>`. **Never use `asp-for` on them** — it emits a companion hidden `false` input that corrupts the list. Keep the property initialised to `new()` and non-nullable: when everything is unchecked nothing posts, and the empty list correctly means "clear all".

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
│   ├── Authorization/  AppClaimTypes.cs, Permissions.cs, AppRoles.cs, PermissionCatalog.cs
│   ├── Enums/          Gender, GuestType, PackageCategory, RoomType, WeekDays
│   ├── Models/         BaseModel, Booking*, Destination, Package*; Models/Identity/AppUser.cs
│   ├── Repositories/IGenericRepository.cs
│   ├── Services/       IFileStorageService.cs, OperationResult.cs,
│   │                   Identity/IRoleService.cs, Identity/IUserAccountService.cs, Itinerary/
│   └── Specifications/ BaseSpecifications.cs, ISpecifications.cs, Bookings/, Packages/
├── Travel.Repository/
│   ├── GenericRepository.cs, SpecificationEvalutor.cs, UnitOfWork.cs
│   ├── Storage/FileStorageService.cs
│   └── Data/TravelContext.cs, AppIdentityDbContextSeed.cs,
│       Configrations/…Config.cs, Migrations/
├── Travel.Services/
│   ├── ItineraryGeneratorService.cs
│   └── Identity/RoleService.cs, UserAccountService.cs
└── Travel.Presentation/
    ├── Program.cs, appsettings.json
    ├── Authorization/  PermissionRequirement, PermissionAuthorizationHandler,
    │                   PermissionPolicyProvider, AppUserClaimsPrincipalFactory
    ├── TagHelpers/PermissionTagHelper.cs
    ├── Helpers/        AvatarHelper.cs, PermissionIcons.cs
    ├── Controllers/    Account, Home, Booking, Package, Destination, Role, User
    ├── ViewModels/, MappingProfiles/
    ├── Views/ (Account, Home, Booking, Package, Destination, Role, User, Shared)
    └── wwwroot/
        ├── css/app-extras.css   (ONLY place for project CSS — see UI/UX Guidelines)
        ├── back/                (Skote template: css/, js/, libs/, images/)
        ├── Files/               (uploads: Packages/, Users/)
        └── lib/, favicon.ico
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

### ⚠️ The template is Bootstrap **5.0.0-beta2**, not 5.3

Before using any Bootstrap utility class, check it exists in `wwwroot/back/assets/css/bootstrap.min.css`. Notably **missing** from this build:
- `bg-*-subtle`, `text-*-emphasis`, `border-*-subtle` (all arrived in 5.3)
- `--bs-primary-rgb` and every other `--bs-*-rgb` variable — so `rgba(var(--bs-primary-rgb), .1)` silently fails; hardcode the rgba
- `btn-soft-*` — Skote's own spelling is the two-class `bg-primary bg-soft`

`bg-*-subtle` and `btn-soft-*` are used ~75 times across 19 views, so rather than rewrite them they are **defined in `wwwroot/css/app-extras.css`**. Palette from `:root`: primary `#556ee6`, secondary `#74788d`, success `#34c38f`, info `#50a5f1`, warning `#f1b44c`, danger `#f46a6a`, dark `#343a40`.

### `wwwroot/css/app-extras.css` — the only place for project CSS

Never edit `bootstrap.min.css` / `app.min.css` (a Skote update replaces them). Linked last in `_Head.cshtml` **and** `_AuthLayout.cshtml`, deliberately **without an `id`**: `back/assets/js/app.js` implements light/dark by rewriting the `href` of `#bootstrap-style` and `#app-style`, so an id-less link survives the swap and always wins the cascade.
> `wwwroot/css/site.css` and `Views/Shared/_Layout.cshtml.css` exist but are **not referenced by anything** — don't put styles there. (`_Layout.cshtml.css` would actually override Skote's primary color if CSS isolation were ever wired up.)

**Dark mode is reachable** via `_RightSideBar` and persists in `sessionStorage`. The swap replaces the whole stylesheet rather than setting `data-bs-theme`, so **there is no dark-mode selector to hook** — new components must be theme-agnostic: build them from `var(--bs-success)` etc. (identical in both themes) and `rgba()` neutrals, never literal `#fff` / `#eff2f7`.

### Boxicons

Many glyphs exist only in the **solid** (`bxs-`) family — `bxs-ship`, `bxs-plane-alt`, `bxs-plane-land`, `bxs-plane-take-off`, `bxs-eraser` — and `bx-refresh` doesn't exist (use `bx-revision`). Verify with `grep "\.bx-name:before" wwwroot/back/assets/css/icons.min.css` before using an icon; a missing one renders as blank space with no error. (`bx-spin` / `bx-tada` are animation modifiers, not glyphs.)

### Permission picker

`Views/Shared/_PermissionModules.cshtml` — a responsive card grid (`row-cols-1 / md-2 / xl-3 / xxl-4`, cards `h-100` so rows align; **no masonry** — Skote ships `masonry.pkgd.min.js` but never initialises it, and it breaks tab order). Each card carries an adaptive **`SELECT ALL` ↔ `DESELECT ALL`** link (`type="button"` — a default `submit` would post the form), rendered only when `group.HasSelectable`.

Three indicator states, styled in `app-extras.css` on a **real `<input type="checkbox">`** (never a div — the form binding, keyboard nav and disabled-doesn't-post behaviour all depend on it):

| State | Look |
|---|---|
| Not granted | empty circle, grey border |
| Granted directly | filled green circle, white check |
| Inherited from a role | green **outline** circle + green check + lock icon, `disabled` |

Catalog descriptions and the "Granted by role: …" source render as **inline muted text** under the label (`.perm-desc` / `.perm-source`) — not tooltips. Only 5 of 26 permissions carry a description, so the height cost is small, and cards use `h-100` so a row still aligns.

### 🚫 Don't use Bootstrap tooltips — show the text inline

Tooltips are **not used anywhere in this project**, and new code shouldn't introduce them. They were tried for permission descriptions and rendered detached from their trigger; the root cause was never fully pinned down (`app.js` double-initialising every `[data-bs-toggle="tooltip"]` before page scripts run was one confirmed factor, and `container: 'body'` did not resolve it). They're also unusable on touch devices and invisible to keyboard users.

**Show the information inline instead** — as a muted second line under the label (`.perm-desc`, `.perm-source` in `app-extras.css`) or as visible badge text. This is what the permission picker, `User/Details` and `_UserForm` all now do. Note `app.js` still runs its own tooltip init over the page; that's vendor code and harmless while no element carries the attribute.

---

## Important Notes

- **`SpecificationEvalutor.cs`** and **`Data/Configrations/`** have intentional typos — do NOT rename them.
- `SuperAdmin` is seeded and assigned on startup; see Authorization System.
- File upload is `IFileStorageService` (Domain) → `FileStorageService` (Repository); `Stream`-based so a future `Travel.Api` can reuse it.
- **`Account/Profile` is still linked from `_Nav` but the action is commented out** — the link 404s. Reviving it needs a `ProfileViewModel` + view.
- **Cookie size:** ~26 permissions ≈ a 2–3 KB encrypted cookie (chunked automatically past 4 KB). Past **~60 permissions**, switch to one comma-joined claim decoded in the handler, or an `ITicketStore`.

---

## Gotchas & Past Mistakes for Claude

> Track every mistake here so it is never repeated.

### 2026-07-17 — Identity registered without `AddEntityFrameworkStores`
**What went wrong:** `AddIdentity<AppUser, IdentityRole>(...)` was configured but never chained to `.AddEntityFrameworkStores<TravelContext>()`, so `IUserStore` couldn't be resolved. This broke the EF **design-time** factory (`dotnet ef migrations add` failed to build the `DbContext`) **and** would crash at runtime (UserManager/SignInManager unconstructable).
**Fix:** chain `.AddEntityFrameworkStores<TravelContext>().AddDefaultTokenProviders()` onto `AddIdentity`.

### 2026-08-01 — Traps hit while building Roles & Permissions
- **A member named `Permissions` shadows the `Permissions` class.** `UserController` needs an action at `/User/Permissions`, but naming the C# method `Permissions` makes every `[Authorize(Policy = Permissions.Users.X)]` in that file fail to compile. **Fix:** name the method `DirectPermissions` and add `[ActionName("Permissions")]`.
- **The base claims factory already unions role claims.** `UserClaimsPrincipalFactory<TUser, TRole>` calls `RoleManager.GetClaimsAsync` for every role, so role permissions were *already* in the cookie. Don't write code to add them — the custom factory only de-duplicates and suppresses claims for SuperAdmin.
- **A `<permission>` TagHelper must not check `User.HasClaim`.** SuperAdmin carries no permission claims (the handler bypasses on the role), so a raw claim check hides the whole UI from the one account that has full access. Go through `IAuthorizationService`.
- **`output.TagName = null` in the TagHelper is load-bearing** — without it the `<permission>` wrapper stays in the DOM and breaks `<ul class="metismenu">`.
- **The TagHelper is C#, not Razor.** `AddRazorRuntimeCompilation` won't pick it up: without a rebuild, `<permission>` renders literally in the page source. That's the symptom to look for.
- **Seed guards must be independent.** The existing `if (!userManager.Users.Any())` is already false on any live DB, so role seeding placed inside it silently never runs. Use a separate `RoleExistsAsync` check.
- **Roles/users can't use `IUnitOfWork.Repository<T>()`** — it's `where T : BaseModel` (int Id), and `IdentityRole`/`AppUser` have string Ids. Use `RoleManager`/`UserManager`. They share the scoped `TravelContext`, so `BeginTransactionAsync` still makes a multi-claim replacement atomic (each `Add/RemoveClaimAsync` is its own `SaveChanges`).
- **Always go through `FindByNameAsync`/`RoleExistsAsync`/`SetRoleNameAsync`.** Identity queries `NormalizedName`; `context.Roles.Add(new IdentityRole("Ops"))` leaves it null and the role becomes invisible to every lookup.
- **Disabled checkboxes don't post** — that's exactly how inherited permissions stay out of the direct-grant list. `readonly` doesn't apply to checkboxes and *would* post.
- **Bootstrap tooltips render detached in this template — don't use them.** Permission descriptions were built as tooltips and appeared far from their icon. Two contributing causes were confirmed (`app.js` initialises every tooltip *before* page scripts, so a page-level `new bootstrap.Tooltip(...)` created a second instance with its own hover listeners; and the default `container: false` puts the tip inside a flex row within a `position: relative` card). Neither de-duplicating nor `container: 'body'` fixed it, so the mechanism was **abandoned entirely** in favour of inline text. Don't re-introduce tooltips expecting them to work — see UI/UX Guidelines.
- **Utility classes that don't exist fail silently.** `bg-*-subtle` and `btn-soft-*` were used ~75 times across 19 views and rendered as bare text / unstyled buttons for months, because a missing CSS class produces no error anywhere. Same for a missing Boxicon. **Grep the stylesheet before using a class or icon** — see the Bootstrap 5.0.0-beta2 note under UI/UX Guidelines.
- **`/Home/Error` needs `[AllowAnonymous]`** once `HomeController` is `[Authorize]`d — it's the `UseExceptionHandler` target, and requiring auth there redirects to Login and swallows the real error.
- **`AccessDeniedPath` pointed at a view that didn't exist**, so every 403 threw "view not found". Adding policy gates makes that path reachable immediately — build the view first.
- **Gate `IsNameAvailable`/`Preview` on `View`, not `Create`.** Both are called from the Edit form too; gating them on Create silently breaks editing.

### Reusable guardrails (from the same architecture — apply as the domain is built)
- **SQL Server cascade cycles (error 1785):** don't set every FK to `Cascade`. Use `Cascade` only for ownership FKs, `NoAction` for reference FKs to break multiple cascade paths to the same table.
- **EF enum default value:** don't `HasDefaultValue(1)` (int) on an enum property — type mismatch at design time. Initialize the default in the C# model instead and omit the DB default.
- **Upload directory:** create the target folder before opening a `FileStream` (`Directory.CreateDirectory` is a safe no-op if it exists). `FileStorageService` already does this — keep it for any new upload folder.
- **Submit button "Saving…" stuck:** gate the disable-button/spinner on validity — `if (!$(form).valid()) return;` *before* disabling — so a client-side validation failure doesn't permanently disable the button.
- **CRLF vs LF edits:** if a targeted string replace silently fails to match, verify with a fresh read and fall back to overwriting the whole file.
