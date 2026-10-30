using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnePondy.Application.Audit;
using OnePondy.Application.Bookings;
using OnePondy.Application.Businesses;
using OnePondy.Application.Configuration;
using OnePondy.Application.Identity;
using OnePondy.Application.Itineraries;
using OnePondy.Application.Listings;
using OnePondy.Application.Media;
using OnePondy.Application.Notifications;
using OnePondy.Application.Payments;
using OnePondy.Application.Places;
using OnePondy.Application.Staff;
using OnePondy.Application.TripPlanner;

namespace OnePondy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));
        services.Configure<ContentOptions>(configuration.GetSection(ContentOptions.SectionName));
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));
        services.Configure<ConsoleOptions>(configuration.GetSection(ConsoleOptions.SectionName));
        services.Configure<TripPlannerOptions>(configuration.GetSection(TripPlannerOptions.SectionName));
        services.Configure<PaymentsOptions>(configuration.GetSection(PaymentsOptions.SectionName));
        services.Configure<OnePondy.Application.Food.FoodOptions>(configuration.GetSection(OnePondy.Application.Food.FoodOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAccountDeletionGuard, BookingsDeletionGuard>();

        services.AddScoped<OtpChallengeService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AuthDataRetention>();

        services.AddScoped<AuditTrail>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<StaffService>();
        services.AddScoped<StaffSignInService>();
        services.AddScoped<PlaceAdminService>();
        services.AddScoped<PlaceCatalogService>();
        services.AddScoped<MediaService>();
        services.AddScoped<ConfigService>();
        services.AddScoped<SavedPlacesService>();
        services.AddScoped<ItineraryService>();
        services.AddScoped<TripPlannerService>();
        services.AddScoped<BusinessService>();
        services.AddScoped<ListingService>();
        services.AddScoped<SavedListingsService>();
        services.AddScoped<BookingService>();
        services.AddScoped<BookingHoldExpiry>();
        services.AddScoped<LedgerService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<ReconciliationExceptionService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<OnePondy.Application.Food.RestaurantService>();
        services.AddScoped<OnePondy.Application.Food.MenuService>();
        services.AddScoped<OnePondy.Application.Food.OrderService>();
        services.AddScoped<OnePondy.Application.Food.FoodOrderExpiry>();
        services.AddScoped<OnePondy.Application.Delivery.DriverService>();
        services.AddScoped<PaymentAdminService>();
        services.AddScoped<NotificationOutbox>();
        services.AddScoped<DeviceService>();
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<BookingReminders>();
        services.AddScoped<NotificationRetention>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentWebhookService>();

        return services;
    }
}
