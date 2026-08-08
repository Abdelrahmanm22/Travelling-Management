using AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models.Identity;
using Travel.Domain.Repositories;
using Travel.Domain.Services;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.CodeGeneration;
using Travel.Domain.Services.Identity;
using Travel.Domain.Services.Itinerary;
using Travel.Presentation.Authorization;
using Travel.Presentation.Services;
using Travel.Repository;
using Travel.Repository.Data;
using Travel.Repository.Storage;
using Travel.Services;
using Travel.Services.BlockServices;
using Travel.Services.CodeGeneration;
using Travel.Services.Identity;

namespace Travel.Presentation
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            #region Configure Serilog
            // Overridable so the dev container can write outside the project tree:
            // dotnet watch treats any file under it as a change, and Serilog writing
            // there makes every log line look like an edit.
            var logPath = Environment.GetEnvironmentVariable("TRAVEL_LOG_PATH") ?? "Logs/log-.txt";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            #endregion

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            #region Configure Services

            builder.Services.AddAutoMapper(typeof(Program).Assembly);
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<IItineraryGeneratorService, ItineraryGeneratorService>();
            builder.Services.AddScoped<ICodeGeneratorService, CodeGeneratorService>();
            builder.Services.AddScoped<IBlockServiceQuery, BlockServiceQueryService>();
            builder.Services.AddScoped<IBlockStatusService, BlockStatusService>();
            builder.Services.AddScoped<BlockExcelExporter>();
            builder.Services.AddScoped<BookingExcelExporter>();
            builder.Services.AddScoped<IFileStorageService>(_ => new FileStorageService(builder.Environment.WebRootPath));
            builder.Services.AddControllersWithViews()
                .AddRazorRuntimeCompilation()
                .AddViewOptions(options =>
                {
                    options.HtmlHelperOptions.ClientValidationEnabled = true;
                });

            builder.Services.AddDbContext<TravelContext>(options =>
            {
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
            });

            builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

            builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
            {
                // Password policy
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;

                // AppUserConfig puts a unique index on Email — let Identity catch duplicates
                // as a clean IdentityResult error instead of a SQL constraint violation.
                options.User.RequireUniqueEmail = true;
            })
                .AddEntityFrameworkStores<TravelContext>()
                .AddDefaultTokenProviders()
                .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

            #region Authorization (permission policies)

            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

            // Re-issues the auth cookie (re-running the claims factory) on this interval, so role and
            // permission changes reach signed-in users without forcing them to log in again.
            builder.Services.Configure<SecurityStampValidatorOptions>(options =>
                options.ValidationInterval = TimeSpan.FromMinutes(2));

            builder.Services.AddScoped<IRoleService, RoleService>();
            builder.Services.AddScoped<IUserAccountService, UserAccountService>();

            #endregion

            // Configure cookie to redirect to Login when unauthenticated
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });

            #endregion

            var app = builder.Build();

            #region Update-Database-on-Startup
            // Retried because a containerised SQL Server can still be refusing connections
            // when the app starts. Failure here is only logged, so without retries the app
            // would come up against an unmigrated database and look broken with no error.
            // Deliberately NOT solved with EnableRetryOnFailure: that execution strategy
            // rejects the explicit transactions in BlockStatusService/RoleService/UserAccountService.
            const int migrationAttempts = 10;
            var migrationRetryDelay = TimeSpan.FromSeconds(3);

            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            for (var attempt = 1; attempt <= migrationAttempts; attempt++)
            {
                try
                {
                    var dbContext = services.GetRequiredService<TravelContext>();
                    await dbContext.Database.MigrateAsync();

                    var gaps = PermissionCatalog.FindCatalogGaps();
                    if (gaps.Count > 0)
                        Log.Warning("Permission catalog is inconsistent: {Gaps}", gaps);

                    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
                    var userManager = services.GetRequiredService<UserManager<AppUser>>();

                    await AppIdentityDbContextSeed.SeedRolesAsync(roleManager);
                    await AppIdentityDbContextSeed.SeedUserAsync(userManager, roleManager);
                    await AppIdentityDbContextSeed.AssignSuperAdminAsync(userManager);

                    var superAdmins = await userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
                    Log.Information("Identity seed complete. {Count} user(s) hold {Role}: {Users}",
                        superAdmins.Count, AppRoles.SuperAdmin, superAdmins.Select(u => u.UserName));
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == migrationAttempts)
                    {
                        Log.Error(ex, "An error occurred while applying database migrations.");
                        break;
                    }

                    Log.Warning(ex, "Database not ready (attempt {Attempt} of {Total}). Retrying in {Delay}s.",
                        attempt, migrationAttempts, migrationRetryDelay.TotalSeconds);
                    await Task.Delay(migrationRetryDelay);
                }
            }
            #endregion

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            // MapStaticAssets (below) only serves assets present in the build-time
            // manifest, so anything IFileStorageService writes at runtime — package
            // covers, user avatars, booking attachments — 404s without this.
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication(); // Must come before UseAuthorization
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                // Start on the Login page by default
                pattern: "{controller=Account}/{action=Login}/{id?}")
                .WithStaticAssets();

            app.Run();
        }
    }
}
