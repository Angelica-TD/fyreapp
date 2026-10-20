using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Auth;
using FyreApp.Hubs;
using FyreApp.Services.Clients;
using FyreApp.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FyreApp.Services;
using FyreApp.Services.Techs;
using FyreApp.Services.UserManagement;
using FyreApp.Services.Proposals;
using FyreApp.Services.ServiceOfferings;
using FyreApp.Services.Email;
using FyreApp.Services.Quotes;
using FyreApp.Services.Imports;
using FyreApp.Services.DataReset;
using FyreApp.Services.MaintenanceSchedules;

var builder = WebApplication.CreateBuilder(args);
var authEnabled = builder.Configuration.GetValue<bool>("Auth:Enabled", true);

// --------------------------------------------------
// Services
// --------------------------------------------------

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IMaintenanceScheduleService, MaintenanceScheduleService>();
builder.Services.AddScoped<FyreApp.Services.Routines.IRoutineListService, FyreApp.Services.Routines.RoutineListService>();
builder.Services.AddScoped<FyreApp.Services.Routines.IRoutineSetupService, FyreApp.Services.Routines.RoutineSetupService>();
builder.Services.AddScoped<FyreApp.Services.Assets.IAssetListService, FyreApp.Services.Assets.AssetListService>();
builder.Services.AddScoped<FyreApp.Services.Lists.IPropertyListService, FyreApp.Services.Lists.PropertyListService>();
builder.Services.AddScoped<FyreApp.Services.Lists.IRemarkListService, FyreApp.Services.Lists.RemarkListService>();
builder.Services.AddScoped<FyreApp.Services.Lists.IReportListService, FyreApp.Services.Lists.ReportListService>();
builder.Services.AddScoped<FyreApp.Services.Billing.IBillingPageService, FyreApp.Services.Billing.BillingPageService>();
builder.Services.AddScoped<FyreApp.Services.Lists.ICorrespondenceListService, FyreApp.Services.Lists.CorrespondenceListService>();
builder.Services.AddScoped<FyreApp.Services.Tasks.ITaskListService, FyreApp.Services.Tasks.TaskListService>();
builder.Services.AddScoped<FyreApp.Services.Tasks.ITaskPageService, FyreApp.Services.Tasks.TaskPageService>();
builder.Services.AddScoped<FyreApp.Services.Lists.IDirectoryListService, FyreApp.Services.Lists.DirectoryListService>();
builder.Services.AddScoped<FyreApp.Services.Lists.IProductListService, FyreApp.Services.Lists.ProductListService>();
builder.Services.AddScoped<FyreApp.Services.Timeline.ITimelineService, FyreApp.Services.Timeline.TimelineService>();
builder.Services.AddScoped<FyreApp.Services.Assets.IAssetPageService, FyreApp.Services.Assets.AssetPageService>();
builder.Services.AddScoped<IDataResetService, DataResetService>();
builder.Services.AddScoped<IClientTaskService, ClientTaskService>();
builder.Services.AddScoped<ITechService, TechService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IProposalService, ProposalService>();
builder.Services.AddScoped<IServiceOfferingService, ServiceOfferingService>();
builder.Services.AddScoped<IQuoteService, QuoteService>();

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

// Uptick API migration (docs/uptick-migration.md): one client for the whole app, so every run shares its
// request budget and token
builder.Services.Configure<FyreApp.Services.Uptick.Api.UptickApiOptions>(builder.Configuration.GetSection("Uptick:Api"));
// Redirects aren't followed: a file download's is a signed link to Uptick's storage, fetched without the API token
builder.Services.AddHttpClient(FyreApp.Services.Uptick.Api.UptickApiClient.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient(FyreApp.Services.Uptick.Files.UptickFileDownloader.HttpClientName);
// Where files are kept (docs/uptick-migration.md §8): Storage:Provider S3 (production) or Local (development)
builder.Services.Configure<FyreApp.Services.Files.FileStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddSingleton<FyreApp.Services.Files.IFileStore>(sp => FyreApp.Services.Files.FileStores.Create(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FyreApp.Services.Files.FileStorageOptions>>(),
    sp.GetRequiredService<IWebHostEnvironment>()));
builder.Services.AddSingleton<FyreApp.Services.Uptick.Api.IUptickApiClient, FyreApp.Services.Uptick.Api.UptickApiClient>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Migration.UptickMigrator>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Files.UptickFileDownloader>();
// Mappers from the raw copy into FyreApp's tables, in the order they run (docs/uptick-migration.md §3.4)
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.BillingCardMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ClientMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.PropertyMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ClientContactMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.PropertyContactMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.UserMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.SecurityGroupMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ContractorMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.SupplierMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.AccreditationMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ExtraFieldMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.PromptQuestionMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.PropertyScheduleMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.AssetTypeMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.AssetTypeVariantMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.AssetMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RoutineServiceTypeMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RoutineServiceLevelMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RemarkTypeMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.AssetTypeRoutineLinksMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ProductMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.WarehouseMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.StockMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.BillingContractMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.BillingContractLineItemMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RoutineServiceMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RoutineServiceLevelLinkMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.TaskMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ServiceTaskMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.DefectQuoteMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ServiceQuoteMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.InvoiceMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.CreditNoteMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.DynamicFormResponseMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RemarkMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ReportMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ReportItemMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RoutineMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.SubtaskMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.TaskSessionMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.AppointmentMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.AppointmentTechniciansMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.TaskStockTransactionMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.DefectQuoteLineMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.InvoiceLineMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ServiceQuoteProductLineMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ServiceQuoteFixedLineMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.ServiceQuoteDoAndChargeLineMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.RemarkEventMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.CorrespondenceMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.CorrespondenceLinkMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.DispatchMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.IUptickMapper, FyreApp.Services.Uptick.Mapping.TimelineEventMapper>();
builder.Services.AddScoped<FyreApp.Services.Uptick.Mapping.UptickMapping>();
builder.Services.AddSingleton<FyreApp.Services.Uptick.Migration.UptickMigrationRunner>();
builder.Services.AddSingleton<FyreApp.Services.Uptick.Migration.IUptickMigrationRunner>(sp =>
    sp.GetRequiredService<FyreApp.Services.Uptick.Migration.UptickMigrationRunner>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<FyreApp.Services.Uptick.Migration.UptickMigrationRunner>());
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<BreadcrumbFilter>();
});



builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;

        // hardenning recommended defaults
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<FyreApp.Services.Properties.GoogleMapsOptions>(
    builder.Configuration.GetSection("GoogleMaps"));

builder.Services.AddHttpClient<FyreApp.Services.Properties.GoogleGeocodingClient>();

builder.Services.AddScoped<FyreApp.Services.Properties.SitesService>();
builder.Services.AddScoped<FyreApp.Services.Properties.IPropertyPageService, FyreApp.Services.Properties.PropertyPageService>();



if (!authEnabled)
{
    // Override the default auth scheme to always authenticate as Admin in dev
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = DevAuthHandler.SchemeName;
        options.DefaultChallengeScheme = DevAuthHandler.SchemeName;
    })
    .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(
        DevAuthHandler.SchemeName, _ => { });
}

// Every page needs a login unless it says otherwise ([AllowAnonymous]: the login page, client quote links),
// so a controller without [Authorize] is never public by mistake
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// --------------------------------------------------
// Auth cookie behaviour (GLOBAL + DRY)
// --------------------------------------------------

builder.Services.ConfigureApplicationCookie(options =>
{
    // UI navigation
    options.LoginPath = "/";
    options.AccessDeniedPath = "/";

    // API / AJAX should never receive HTML redirects
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (!authEnabled)
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }
        if (IsApiOrAjaxRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (!authEnabled)
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }
        if (IsApiOrAjaxRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect("/");
        return Task.CompletedTask;
    };
});

builder.Services.AddRazorPages();

// --------------------------------------------------
// App
// --------------------------------------------------

var app = builder.Build();

// --------------------------------------------------
// Database init (dev only)
// --------------------------------------------------

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    context.Database.Migrate();
    DbInitialiser.Seed(context);
}

// The roles the app uses must exist (in every environment), or creating e.g. a technician fails with
// "Role TECH does not exist". Only adds missing ones.
using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in FyreApp.Services.UserManagement.UserManagementService.Roles)
        if (!await roles.RoleExistsAsync(role))
            await roles.CreateAsync(new IdentityRole(role));
}

// --------------------------------------------------
// Middleware
// --------------------------------------------------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// CSS, JS and images are public (the login page needs them)
app.MapStaticAssets().AllowAnonymous();

app.MapHub<ImportProgressHub>("/hubs/importProgress");

// --------------------------------------------------
// Routing
// --------------------------------------------------

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();

app.Run();

// --------------------------------------------------
// Helpers (DRY)
// --------------------------------------------------

static bool IsApiOrAjaxRequest(HttpRequest request)
{
    // Route-based API detection
    if (request.Path.StartsWithSegments("/api"))
        return true;

    // AJAX / fetch
    if (request.Headers.TryGetValue("X-Requested-With", out var xrw) &&
        xrw == "XMLHttpRequest")
        return true;

    // JSON clients (FullCalendar, fetch, etc.)
    if (request.Headers.TryGetValue("Accept", out var accept) &&
        accept.Any(a => a.Contains("application/json", StringComparison.OrdinalIgnoreCase)))
        return true;

    return false;
}
