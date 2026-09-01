using System.Globalization;
using BricksAndBooks.Data;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Data.Firestore;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using Google.Api.Gax.Grpc;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Use South African formatting for all standard date and currency output across the application.
var southAfricanCulture = CultureInfo.GetCultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = southAfricanCulture;
CultureInfo.DefaultThreadCurrentUICulture = southAfricanCulture;

var firebaseProjectId = builder.Configuration["Firebase:ProjectId"]
    ?? throw new InvalidOperationException("Firebase:ProjectId must be configured.");
var firebaseDatabaseId = (builder.Configuration["Firebase:DatabaseId"] ?? "(default)").Trim();
if (string.IsNullOrWhiteSpace(firebaseDatabaseId))
{
    firebaseDatabaseId = "(default)";
}

// Firestore uses Application Default Credentials. Locally, set GOOGLE_APPLICATION_CREDENTIALS
// to an uncommitted service-account JSON file; do not put credentials in appsettings.json.
// An explicit HttpClientHandler avoids the default .NET gRPC connectivity path that fails on
// some Windows development machines with "Unable to get subchannel from HttpRequestMessage".
builder.Services.AddSingleton(_ =>
{
    var grpcAdapter = GrpcNetClientAdapter.Default.WithAdditionalOptions(options =>
    {
        options.HttpHandler = new HttpClientHandler { UseProxy = false };
    });

    return new FirestoreDbBuilder
    {
        ProjectId = firebaseProjectId,
        DatabaseId = firebaseDatabaseId,
        GrpcAdapter = grpcAdapter
    }.Build();
});
builder.Services.AddSingleton<FirestorePersistenceService>();
builder.Services.AddSingleton<FirestoreSaveChangesInterceptor>();

// The existing MVC application and ASP.NET Identity use DbContext extensively. The in-memory
// provider retains those code paths, while FirestorePersistenceService makes Firestore the only
// durable source of truth and reloads the cache when the app starts.
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options.UseInMemoryDatabase("BricksAndBooksCompatibilityCache")
        .AddInterceptors(serviceProvider.GetRequiredService<FirestoreSaveChangesInterceptor>()))
    .AddScoped<IRepositoryWrapper, RepositoryWrapper>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllersWithViews();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = false;
    options.Password.RequiredUniqueChars = 6;

    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
    options.Lockout.MaxFailedAccessAttempts = 10;
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
});

builder.Services.AddScoped<TariffCalculationService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<PaymentAnomalyDetectionService>();
builder.Services.AddScoped<AuditService>();

var app = builder.Build();
app.UseForwardedHeaders();

// Rebuild the short-lived local cache from Firestore before the existing application seed runs.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();

        var firestorePersistence = services.GetRequiredService<FirestorePersistenceService>();
        await firestorePersistence.HydrateAsync(context);
        SeedData.Initialize(services);
        await firestorePersistence.EnsureMetadataAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "The application could not initialize its Firestore-backed data store.");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
