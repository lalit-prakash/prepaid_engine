using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Security;
using PrepaidEngine.Api.Dashboard;
using PrepaidEngine.Api.Health;
using PrepaidEngine.Api.Endpoints;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Api.Reports.ReportJobs;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Conversion;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Sla;
using PrepaidEngine.Infrastructure.Tariffs;
using PrepaidEngine.Application.Sla;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using PrepaidEngine.Infrastructure.Rms;

// Helper for operators: prints a PBKDF2 hash to put in DemoAuth:Users[].PasswordHash.
if (args.Length == 2 && args[0] == "hash-password")
{
    Console.WriteLine(PasswordHasher.Hash(args[1]));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<PrepaidEngineDbContext>((serviceProvider, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PrepaidEngine"))
        .AddInterceptors(serviceProvider.GetRequiredService<AuditContextInterceptor>()));

// TODO(RMS integration): swap for a real HTTP-based IRmsClient adapter once RMS's API
// contract is available; keep MockRmsClient registered for local dev / tests until then.
builder.Services.AddSingleton<IRmsClient, MockRmsClient>();

// TODO(meter-command integration): swap for a real adapter (STS/DLMS/COSEM/vendor API) once
// one exists; keep MockMeterCommandClient registered for local dev / tests until then.
builder.Services.AddSingleton<IMeterCommandClient, MockMeterCommandClient>();

// Recharges only queue the meter credit command; this worker delivers it (see MeterCommandDispatcher).
builder.Services.Configure<PrepaidEngine.Api.MeterCommands.MeterCommandWorkerOptions>(builder.Configuration.GetSection(PrepaidEngine.Api.MeterCommands.MeterCommandWorkerOptions.SectionName));
builder.Services.AddScoped<PrepaidEngine.Infrastructure.MeterCommands.MeterCommandDispatcher>();
builder.Services.AddHostedService<PrepaidEngine.Api.MeterCommands.MeterCommandWorker>();

// TODO(connectivity-command integration): swap for a real adapter once one exists; keep
// MockConnectivityCommandClient registered for local dev / tests until then.
builder.Services.AddSingleton<IConnectivityCommandClient, MockConnectivityCommandClient>();

// TODO(MDMS/HES integration): swap for a real adapter that actually carries a payment-mode-change
// command down the MDMS -> HES -> Meter chain; keep MockPaymentModeChangeClient registered for
// local dev / tests until then.
builder.Services.AddSingleton<IPaymentModeChangeClient, MockPaymentModeChangeClient>();

// Central emergency-credit disconnect/reconnect policy — see IEmergencyCreditGuard's doc comment.
// Scoped since it holds a scoped PrepaidEngineDbContext.
builder.Services.AddScoped<IEmergencyCreditGuard, EmergencyCreditGuard>();

// The DLP billing pipeline service — see IBillingEngineService's doc comment. Scoped (not
// singleton) since it holds a scoped PrepaidEngineDbContext.
builder.Services.Configure<PrepaidEngine.Application.Wallets.LowBalanceOptions>(builder.Configuration.GetSection(PrepaidEngine.Application.Wallets.LowBalanceOptions.SectionName));
if (!(builder.Configuration.GetSection(PrepaidEngine.Application.Wallets.LowBalanceOptions.SectionName).Get<PrepaidEngine.Application.Wallets.LowBalanceOptions>() ?? new()).IsValid)
    throw new InvalidOperationException("LowBalance:ThresholdRs must be zero or more.");
builder.Services.AddScoped<IBillingEngineService, BillingEngineService>();

// Full report exports built in the background (see Reports/ReportJobs).
builder.Services.Configure<PrepaidEngine.Api.Reports.ReportJobs.ReportJobOptions>(builder.Configuration.GetSection(PrepaidEngine.Api.Reports.ReportJobs.ReportJobOptions.SectionName));
builder.Services.AddScoped<PrepaidEngine.Api.Reports.ReportJobs.ReportJobRunner>();
builder.Services.AddHostedService<PrepaidEngine.Api.Reports.ReportJobs.ReportJobWorker>();

// Daily wallet totals for the balance-history chart (see DailyWalletStat).
builder.Services.AddScoped<PrepaidEngine.Infrastructure.Wallets.WalletStatsService>();
builder.Services.AddHostedService<PrepaidEngine.Api.Wallets.WalletStatsWorker>();

// BP/LS/IP/Events ingestion + cross-source energy validation — see IMeterDataIngestionService's
// doc comment. Never bills anything; DLP billing stays entirely in IBillingEngineService above.
builder.Services.Configure<EnergyValidationOptions>(builder.Configuration.GetSection(EnergyValidationOptions.SectionName));
builder.Services.AddScoped<IMeterDataIngestionService, MeterDataIngestionService>();

// Real SLA performance for DLP ingestion/billing/recharge/meter-credit/RC-DC — see
// ISlaMonitoringService's doc comment. Targets configurable via appsettings.json.
builder.Services.Configure<SlaMonitoringOptions>(builder.Configuration.GetSection(SlaMonitoringOptions.SectionName));
builder.Services.AddScoped<ISlaMonitoringService, SlaMonitoringService>();

// Local/demo background processing for the daily billing cycle — see the worker's own doc
// comment for why this is intentionally simple.
builder.Services.AddHostedService<PrepaidEngine.Api.Billing.BillingProcessingWorker>();

// Activates approved tariff changes when their commencement date arrives (see TariffActivationService).
builder.Services.AddScoped<TariffActivationService>();
builder.Services.AddHostedService<PrepaidEngine.Api.Tariffs.TariffActivationWorker>();

// JWT bearer authentication: POST /api/v1/auth/login issues a short-lived signed token (see Auth/).
// The signing key is a secret (user-secrets or Jwt__Key). Development falls back to a random per-run
// key so local work needs no setup (tokens then die on restart); any other environment must configure one.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:Key is not configured. Set it via user-secrets or the Jwt__Key environment variable (at least 32 characters).");
    jwtOptions.Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}
else if (jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");
}
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(jwtOptions));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PrepaidEngine.Api.Health.WorkerStatusRegistry>();
builder.Services.AddSingleton<AuditContextInterceptor>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<TokenService>();
builder.Services.Configure<PrepaidEngine.Api.Auth.Email.EmailOptions>(builder.Configuration.GetSection(PrepaidEngine.Api.Auth.Email.EmailOptions.SectionName));
// Real SMTP when Email:Host is set. Without it, Development prints messages to the console; any other environment sends nothing (and the reset says so).
if (!string.IsNullOrWhiteSpace(builder.Configuration["Email:Host"]))
    builder.Services.AddSingleton<PrepaidEngine.Api.Auth.Email.IEmailSender, PrepaidEngine.Api.Auth.Email.SmtpEmailSender>();
else if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<PrepaidEngine.Api.Auth.Email.IEmailSender, PrepaidEngine.Api.Auth.Email.ConsoleEmailSender>();
else
    builder.Services.AddSingleton<PrepaidEngine.Api.Auth.Email.IEmailSender, PrepaidEngine.Api.Auth.Email.NoEmailSender>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = TokenService.SigningKey(jwtOptions),
            ValidAlgorithms = new[] { Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256 },
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
    });

// Two-role authorization for the tariff-governance workflow (see UserRole's doc comment) — IT
// drafts/edits/submits, Utility reviews/approves/rejects. Every pre-existing endpoint keeps using
// plain .RequireAuthorization() (no role requirement), so this is additive, not a breaking change.
builder.Services.AddAuthorization(options =>
{
    // Deny by default: every write endpoint must name one of these policies (checked at startup below).
    // Read endpoints use plain RequireAuthorization(), so any signed-in role, including ReadOnly, can read.
    const string admin = nameof(UserRole.Admin), it = nameof(UserRole.IT), utility = nameof(UserRole.Utility), op = nameof(UserRole.Operator);
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
    // Operational actions: recharge, disconnect/reconnect, retries, conversions, reconciliation, exceptions, replacements, holds, alarms.
    options.AddPolicy("Operations", policy => policy.RequireRole(admin, it, op));
    // Bulk data and billing runs.
    options.AddPolicy("DataAdmin", policy => policy.RequireRole(admin, it));
    // Tariff governance: drafting is IT (or Admin); approving is Utility only, so no one approves their own change.
    options.AddPolicy("ITRole", policy => policy.RequireRole(admin, it));
    options.AddPolicy("UtilityRole", policy => policy.RequireRole(utility));
    options.AddPolicy("TariffGovernanceRole", policy => policy.RequireRole(admin, it, utility));
});

// CORS, rate limiting, request size limits and forwarded-header handling (see Security/ and docs/assumptions-and-security.md).
var securityOptions = builder.AddApiSecurity();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Local/demo convenience only: apply any pending migrations and seed sample data.
    // Never runs outside Development, so production deployments must migrate explicitly.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PrepaidEngineDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
    await DbSeeder.SeedExtraConsumersAsync(db);
    await DbSeeder.SeedDemoNetworkAsync(db);

}

app.UseApiSecurity(securityOptions);

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapReportEndpoints();
app.MapReportJobEndpoints();
app.MapNetworkEndpoints();
app.MapPlatformEndpoints();
app.MapConsumerEndpoints();
app.MapConnectivityEndpoints();
app.MapBillingEndpoints();
app.MapTariffEndpoints();
app.MapRechargeEndpoints();
app.MapConversionEndpoints();
app.MapOperationsEndpoints();
app.MapAuditEndpoints();
app.MapAnalyticsEndpoints();
app.MapMeterDataEndpoints();
app.MapPagedListEndpoints();
app.MapSystemEndpoints();

// Writes docs/API_REFERENCE.md from the registered endpoints, then exits (see Docs/ApiReference.cs).
if (args.Length >= 1 && args[0] == "dump-endpoints")
{
    // The API description provider only sees endpoints once the host has started, so start it (on an ephemeral port) and stop it again.
    await app.StartAsync();
    await PrepaidEngine.Api.Docs.ApiReference.WriteAsync(app, args.Length >= 2 ? args[1] : "API_REFERENCE.md");
    await app.StopAsync();
    return;
}

// Deny-by-default guard: refuse to start if any write endpoint (other than sign-in) has no named authorization policy.
// The endpoints are read from the app's own route builder: the DI EndpointDataSource is empty until the host starts, so a
// guard that used it would check nothing. An empty list is itself an error, so the guard can never pass silently.
{
    var endpoints = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
        .SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
    if (endpoints.Count == 0)
        throw new InvalidOperationException("The authorization guard found no endpoints to check.");

    var unprotected = endpoints
        .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>() is { } m
                    && m.HttpMethods.Any(h => h is "POST" or "PUT" or "PATCH" or "DELETE")
                    && e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is null
                    && !e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any(a => !string.IsNullOrEmpty(a.Policy)))
        .Select(e => e.RoutePattern.RawText)
        .ToList();
    if (unprotected.Count > 0)
        throw new InvalidOperationException("Write endpoints without an authorization policy: " + string.Join(", ", unprotected));
}

app.Run();

public partial class Program { }
