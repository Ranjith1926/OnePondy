using OnePondy.Application;
using OnePondy.Infrastructure;
using OnePondy.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Runs as a Windows Service when installed as one, and as a console app otherwise (spec §13).
builder.Services.AddWindowsService(options => options.ServiceName = "OnePondy Worker");

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Further jobs (outbox dispatch, hold expiry, reconciliation) are added in the phases that need them.
builder.Services.AddHostedService<AuthDataRetentionJob>();

var host = builder.Build();
host.Run();
