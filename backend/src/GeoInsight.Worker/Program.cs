using GeoInsight.Infrastructure;
using Hangfire;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// This is the one place AddHangfireServer() is called in the whole solution —
// GeoInsight.Api only enqueues jobs (via the same AddHangfire(...) call inside
// AddInfrastructure) so an HTTP request is never blocked by geocoding work.
builder.Services.AddHangfireServer();

var host = builder.Build();
host.Run();
