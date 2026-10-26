var builder = Host.CreateApplicationBuilder(args);

// Runs as a Windows Service when installed as one, and as a console app otherwise (spec §13).
builder.Services.AddWindowsService(options => options.ServiceName = "OnePondy Worker");

// Background jobs (outbox dispatch, hold expiry, reconciliation) are added in the phases that need them.

var host = builder.Build();
host.Run();
