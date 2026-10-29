using OnePondy.Application;
using OnePondy.Infrastructure;
using OnePondy.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Runs as a Windows Service when installed as one, and as a console app otherwise (spec §13).
builder.Services.AddWindowsService(options => options.ServiceName = "OnePondy Worker");

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Further jobs (outbox dispatch, reconciliation) are added in the phases that need them.
builder.Services.AddHostedService<AuthDataRetentionJob>();
builder.Services.AddHostedService<BookingHoldExpiryJob>();
builder.Services.AddHostedService<FoodOrderExpiryJob>();
builder.Services.AddHostedService<PendingRefundJob>();
builder.Services.AddHostedService<PaymentReconciliationJob>();
builder.Services.AddHostedService<OutboxDispatchJob>();
builder.Services.AddHostedService<BookingReminderJob>();

var host = builder.Build();
host.Run();
