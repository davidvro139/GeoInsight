using System.Text;
using System.Text.Json.Serialization;
using GeoInsight.Infrastructure;
using GeoInsight.Infrastructure.Auth;
using GeoInsight.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NetTopologySuite.IO.Converters;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendCorsPolicy";
var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"] ?? "http://localhost:5173";

// Serialize enums (OrganizationRole, etc.) as their string names, not raw ints —
// the frontend's TypeScript types are string unions, and clients calling the API
// directly would otherwise have to know the numeric-vs-name mapping.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Lets Geometry-typed DTO properties (CensusTract boundaries) serialize
        // directly as embedded GeoJSON geometry objects.
        options.JsonSerializerOptions.Converters.Add(new GeoJsonConverterFactory());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(frontendOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("Missing Jwt configuration section.");

        // Without this, the handler silently rewrites "sub" to the long legacy
        // ClaimTypes.NameIdentifier URI, and JwtRegisteredClaimNames.Sub lookups
        // (see ControllerBaseExtensions.GetUserId) never match.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    if (app.Configuration.GetValue("Database:AutoMigrate", false))
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GeoInsightDbContext>();
        dbContext.Database.Migrate();
    }

    app.UseSwagger();
    app.UseSwaggerUI();

    // Development-only: a real production deployment would need a proper
    // Dashboard authorization filter (Hangfire's default only allows local
    // requests, which is already a reasonable stand-in but not real auth).
    app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("HealthCheck");

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
