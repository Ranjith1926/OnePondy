namespace OnePondy.Api.Http;

/// <summary>
/// Decides whether the API reference page (<c>/scalar</c>) and the API description (<c>/openapi/v1.json</c>) are served. They are on in
/// Development, and on a Testing site only when <c>ApiDocs:Enabled</c> is set to true. They are never served in Production, whatever the
/// setting says, because they list every endpoint, including the staff ones.
/// </summary>
public static class ApiDocs
{
    public const string SettingName = "ApiDocs:Enabled";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment()
        || (!environment.IsProduction() && configuration.GetValue<bool>(SettingName));
}
