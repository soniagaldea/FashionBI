using FashionDataAnalysisPlatform.Data;
using Microsoft.EntityFrameworkCore;
using FashionDataAnalysisPlatform.Services;
using FashionDataAnalysisPlatform.Models;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name      = "FashionBI.Auth";
        options.Cookie.HttpOnly  = true;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient("StoreSync", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<StoreSyncBackgroundService>();
builder.Services.AddScoped<ForecastingService>();
builder.Services.AddScoped<SmartInsightsService>();
builder.Services.AddScoped<SustainabilityService>();
builder.Services.AddScoped<NotificationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    context.Database.EnsureCreated();

    // Ensure forecasting tables exist in databases created before this module was added
    context.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ForecastResults' AND xtype='U')
        CREATE TABLE ForecastResults (
            ForecastResultId      INT IDENTITY(1,1) PRIMARY KEY NOT NULL,
            StoreId               INT NULL,
            StoreName             NVARCHAR(100) NOT NULL,
            Category              NVARCHAR(100) NOT NULL,
            ForecastMonth         DATETIME2     NOT NULL,
            RevenueForecast       DECIMAL(18,2) NOT NULL,
            OrdersForecast        INT           NOT NULL,
            UnitsForecast         INT           NOT NULL,
            GeneratedAt           DATETIME2     NOT NULL
        )");

    context.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ForecastAccuracies' AND xtype='U')
        CREATE TABLE ForecastAccuracies (
            ForecastAccuracyId    INT IDENTITY(1,1) PRIMARY KEY NOT NULL,
            Target                NVARCHAR(20)  NOT NULL,
            MAE                   DECIMAL(18,4) NOT NULL,
            RMSE                  DECIMAL(18,4) NOT NULL,
            MAPE                  DECIMAL(18,4) NOT NULL,
            AccuracyPercent       DECIMAL(5,2)  NOT NULL,
            GeneratedAt           DATETIME2     NOT NULL
        )");

    context.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ForecastFeatureImportances' AND xtype='U')
        CREATE TABLE ForecastFeatureImportances (
            ForecastFeatureImportanceId INT IDENTITY(1,1) PRIMARY KEY NOT NULL,
            FeatureName                 NVARCHAR(100) NOT NULL,
            Importance                  DECIMAL(10,6) NOT NULL,
            Target                      NVARCHAR(20)  NOT NULL,
            GeneratedAt                 DATETIME2     NOT NULL
        )");

    // Add ModelName column to tables created before the multi-model comparison feature
    context.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT * FROM sys.columns
            WHERE object_id = OBJECT_ID('ForecastResults') AND name = 'ModelName'
        )
        ALTER TABLE ForecastResults ADD ModelName NVARCHAR(50) NULL");

    context.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT * FROM sys.columns
            WHERE object_id = OBJECT_ID('ForecastAccuracies') AND name = 'ModelName'
        )
        ALTER TABLE ForecastAccuracies ADD ModelName NVARCHAR(50) NULL");

    if (!context.StoreConnections.Any())
    {
        context.StoreConnections.Add(new StoreConnection
        {
            StoreName = "FashionStoreAPI",
            StoreApiUrl = "https://localhost:7151",
            IsActive = true,
            LastSyncAt = null
        });

        context.SaveChanges();
    }
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
