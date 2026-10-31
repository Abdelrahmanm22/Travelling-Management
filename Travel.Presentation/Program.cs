using AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models.Identity;
using Travel.Domain.Repositories;
using Travel.Domain.Services;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.CodeGeneration;
using Travel.Domain.Services.Email;
using Travel.Domain.Services.Expenses;
using Travel.Domain.Services.Identity;
using Travel.Domain.Services.Itinerary;
using Travel.Presentation.Authorization;
using Travel.Presentation.Logging;
using Travel.Presentation.Services;
using Travel.Presentation.Services.Email;
using Travel.Presentation.Services.Vouchers;
using Travel.Repository;
using Travel.Repository.Data;
using Travel.Repository.Data.Auditing;
using Travel.Repository.Storage;
using Travel.Services;
using Travel.Services.BlockServices;
using Travel.Services.CodeGeneration;
using Travel.Services.Expenses;
using Travel.Services.Identity;

namespace Travel.Presentation
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            #region Configure Serilog
            // Bootstrap logger covers the few lines before the host is built; UseSerilog below then
            // reloads it with the real configuration, which needs DI for the HTTP-context enricher.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateBootstrapLogger();
            #endregion

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog((context, services, logger) =>
            {
                // Same line shape on the console (docker compose logs) and in the file. Message:l, not
                // :lj — the JSON formatter would print every enum in quotes ("Deposit").
                const string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Env}] [{UserName}] [{RequestId}] {Source}: {Message:l}{NewLine}{Exception}";

                logger
                    .MinimumLevel.Information()
                    // Framework chatter (a line per SQL command, per request) would bury the business
                    // events. Its warnings and errors — unhandled exceptions, failed saves, antiforgery —
                    // still come through.
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Migrations", LogEventLevel.Information)
                    // Not Microsoft.AspNetCore.Authorization: every sidebar <permission> check that fails
                    // logs "Authorization failed", ~30 lines per page for a limited user. A real denial
                    // is logged once, by AccountController.AccessDenied.
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Env", context.HostingEnvironment.EnvironmentName)
                    .Enrich.With(new HttpContextEnricher(services.GetRequiredService<IHttpContextAccessor>()))
                    .WriteTo.Console(outputTemplate: template)
                    .WriteTo.File(
                        path: "Logs/log-.txt",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: true,
                        outputTemplate: template);
            });

            #region Configure Services

            builder.Services.AddAutoMapper(typeof(Program).Assembly);
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<IItineraryGeneratorService, ItineraryGeneratorService>();
            builder.Services.AddScoped<ICodeGeneratorService, CodeGeneratorService>();
            builder.Services.AddScoped<IBlockServiceQuery, BlockServiceQueryService>();
            builder.Services.AddScoped<IBlockStatusService, BlockStatusService>();
            builder.Services.AddScoped<BlockExcelExporter>();
            builder.Services.AddScoped<BookingExcelExporter>();
            builder.Services.AddScoped<FlightReportExcelExporter>();
            builder.Services.AddScoped<RevenueReportExcelExporter>();
            builder.Services.AddScoped<TransactionExcelExporter>();
            builder.Services.AddScoped<IExpenseCalculator, ExpenseCalculator>();
            builder.Services.AddScoped<IExpenseChangeDetector, ExpenseChangeDetector>();
            builder.Services.AddScoped<SafeLedgerExcelExporter>();
            builder.Services.AddScoped<ExpenseExcelExporter>();
            builder.Services.AddScoped<IFileStorageService>(_ => new FileStorageService(builder.Environment.WebRootPath));

            // Booking voucher (PDF + email). QuestPDF's Community licence covers organisations under
            // US$1M annual revenue; the fonts must be registered before the first document renders.
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            VoucherAssets.RegisterFonts();
            builder.Services.Configure<VoucherOptions>(builder.Configuration.GetSection("Voucher"));
            builder.Services.AddSingleton<VoucherAssets>();
            builder.Services.AddScoped<VoucherModelFactory>();
            // Host and credentials come from app.env on the VPS and from user-secrets locally — never committed.
            builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
            builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();

            // Who is making the request — needed by the audit interceptor (which lives in
            // Travel.Repository and must not see IHttpContextAccessor) and by the transaction ledger.
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            builder.Services.AddScoped<BookingAuditInterceptor>();
            var mvc = builder.Services.AddControllersWithViews(options =>
                    options.Filters.Add<ModelStateLoggingFilter>())
                .AddViewOptions(options =>
                {
                    options.HtmlHelperOptions.ClientValidationEnabled = true;
                });
            // Views are precompiled at build time; recompiling on edit is a local-development convenience.
            if (builder.Environment.IsDevelopment())
                mvc.AddRazorRuntimeCompilation();

            // Deployed behind Caddy, which terminates HTTPS. Trusting its X-Forwarded-* headers is what tells
            // the app the request was HTTPS (secure cookies, no redirect loop). The container is reachable
            // only through Caddy, so the default loopback-only proxy allow-list is cleared.
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            // In a container the default key ring dies with every deploy, signing everyone out and failing
            // every open form's antiforgery token. The deploy mounts a persistent folder and points this at it.
            var keysPath = builder.Configuration["DataProtection:KeysPath"];
            if (!string.IsNullOrEmpty(keysPath))
                builder.Services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
                    .SetApplicationName("Travel");

            builder.Services.AddHealthChecks();

            builder.Services.AddDbContext<TravelContext>((sp, options) =>
            {
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
                // Resolved from the provider rather than newed up: it needs the scoped ICurrentUserService,
                // and the trail is only as useful as the name attached to each entry.
                options.AddInterceptors(sp.GetRequiredService<BookingAuditInterceptor>());
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
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
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
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while applying database migrations.");
            }
            #endregion

            // Configure the HTTP request pipeline.
            app.UseForwardedHeaders();

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

            // Anonymous liveness probe for the deploy pipeline.
            app.MapHealthChecks("/healthz");

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
