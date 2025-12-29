using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// Services
// --------------------------------------------------

builder.Services.AddControllersWithViews();

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
