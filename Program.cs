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

var builder = WebApplication.CreateBuilder(args);
var authEnabled = builder.Configuration.GetValue<bool>("Auth:Enabled", true);

// --------------------------------------------------
// Services
// --------------------------------------------------

builder.Services.AddSingleton<IImportTracker, ImportTracker>();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IClientImportService, ClientImportService>();
builder.Services.AddSignalR();

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IClientTaskService, ClientTaskService>();
builder.Services.AddScoped<ITechService, TechService>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<BreadcrumbFilter>();
});



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

builder.Services.Configure<FyreApp.Services.Sites.GoogleMapsOptions>(
    builder.Configuration.GetSection("GoogleMaps"));

builder.Services.AddHttpClient<FyreApp.Services.Sites.GoogleGeocodingClient>();

builder.Services.AddScoped<FyreApp.Services.Sites.SitesService>();



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

app.MapStaticAssets();

app.MapHub<ImportProgressHub>("/hubs/importProgress");

// --------------------------------------------------
// Routing
// --------------------------------------------------

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();

// --------------------------------------------------
// Identity seeding
// --------------------------------------------------

await IdentitySeed.SeedAdminAsync(app.Services);

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
