using OnePondy.Application.Common;
using OnePondy.Domain.Businesses;

namespace OnePondy.Application.Businesses;

public static class BusinessInputValidator
{
    public const int MaxNameLength = 180;
    public const int MaxDescriptionLength = 2000;
    public const int MaxPhoneLength = 32;
    public const int MaxEmailLength = 320;
    public const int MaxAddressLength = 500;
    public const int MaxGstinLength = 15;
    public const int MaxFssaiLength = 14;

    public static Result<BusinessContent> Validate(BusinessApplicationInput input)
    {
        var errors = new FieldErrors();
        var name = errors.Required("name", input.Name, MaxNameLength);
        var description = errors.Optional("description", input.Description, MaxDescriptionLength);
        var phoneNumber = errors.Optional("phoneNumber", input.PhoneNumber, MaxPhoneLength);
        var email = errors.Optional("email", input.Email, MaxEmailLength);
        var address = errors.Optional("address", input.Address, MaxAddressLength);
        var gstin = errors.Optional("gstin", input.Gstin, MaxGstinLength);
        var fssaiLicenseNumber = errors.Optional("fssaiLicenseNumber", input.FssaiLicenseNumber, MaxFssaiLength);

        if ((input.Latitude is null) != (input.Longitude is null))
        {
            errors.Add("latitude", "Provide both latitude and longitude, or neither.");
        }
        else if (input.Latitude is { } lat and (< -90 or > 90))
        {
            errors.Add("latitude", "Enter a valid latitude.");
        }
        else if (input.Longitude is { } lng and (< -180 or > 180))
        {
            errors.Add("longitude", "Enter a valid longitude.");
        }

        return errors.Any
            ? errors.ToError()
            : new BusinessContent(
                name,
                input.BusinessType,
                description,
                phoneNumber,
                email,
                address,
                input.Latitude,
                input.Longitude,
                gstin,
                fssaiLicenseNumber);
    }
}
