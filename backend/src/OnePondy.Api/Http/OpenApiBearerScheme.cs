using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OnePondy.Api.Http;

/// <summary>
/// Tells the API description (and so the Scalar page) how signed-in endpoints are called: with the access token from sign-in as a
/// bearer token. The scheme is declared once, and attached only to the endpoints that need a signed-in person, so the public ones
/// do not show a lock.
/// </summary>
public static class OpenApiBearerScheme
{
    public const string Name = "Bearer";

    public static OpenApiOptions AddBearerScheme(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "OnePondy API";
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[Name] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The access token returned by sign-in (POST /api/v1/auth/otp/verify, or /api/v1/admin/auth/otp/verify for staff). Paste the token only, without the word Bearer.",
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var needsSignIn = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
            if (needsSignIn)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(Name, context.Document)] = [],
                });
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
