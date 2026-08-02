using AgriERP.Application.Features.Auth.Dtos;
using AgriERP.Application.Features.Masters.Dtos;
using AgriERP.Application.Features.Items.Dtos;
using FluentValidation;

namespace AgriERP.Application.Common.Validators;

/*
 * FluentValidation covers SHAPE: required, length, format, range.
 *
 * Rules needing a database lookup - duplicate codes, whether a itemSubGroup
 * exists, whether batch tracking can be switched off - live in the services,
 * because a validator that queries the database turns every request into an
 * unpredictable number of round trips and still cannot close the race that the
 * unique index closes.
 */

/// <summary>
/// GSTIN: 2-digit state code, 10-character PAN, entity number, 'Z', checksum.
/// Validating the shape here catches the common typo; only the GST portal can
/// confirm a number is real.
/// </summary>
internal static class CommonRules
{
    public const string GstinPattern = @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$";
    public const string PanPattern = @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$";
    public const string IfscPattern = @"^[A-Z]{4}0[A-Z0-9]{6}$";
    public const string PincodePattern = @"^[1-9][0-9]{5}$";
    // Indian mobile numbers start 6-9. Rejecting a leading 0-5 catches a
    // landline typed into the mobile field, which would break SMS later.
    public const string MobilePattern = @"^[6-9][0-9]{9}$";
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("Username is required.").MaximumLength(150);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.").MaximumLength(128);
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128)
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}

public class SaveItemSubGroupRequestValidator : AbstractValidator<SaveItemSubGroupRequest>
{
    public SaveItemSubGroupRequestValidator()
    {
        RuleFor(x => x.ItemSubGroupCode).NotEmpty().MaximumLength(20)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Code may contain letters, digits, hyphen and underscore only.");
        RuleFor(x => x.ItemSubGroupName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.IconName).MaximumLength(40);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public class SaveCompanyRequestValidator : AbstractValidator<SaveCompanyRequest>
{
    public SaveCompanyRequestValidator()
    {
        RuleFor(x => x.CompanyCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.GstNumber).Matches(CommonRules.GstinPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.GstNumber))
            .WithMessage("GST number must be 15 characters, e.g. 27AAAAA0000A1Z5.");
        RuleFor(x => x.Pincode).Matches(CommonRules.PincodePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Pincode))
            .WithMessage("Pincode must be 6 digits.");
        RuleFor(x => x.Email).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.City).MaximumLength(80);
        RuleFor(x => x.Phone).MaximumLength(15);
        RuleFor(x => x.Website).MaximumLength(200);
        RuleFor(x => x.ContactPerson).MaximumLength(120);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public class SaveShopRequestValidator : AbstractValidator<SaveShopRequest>
{
    public SaveShopRequestValidator()
    {
        RuleFor(x => x.ShopName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(80);
        RuleFor(x => x.OwnerName).MaximumLength(120);
        RuleFor(x => x.GstNo).Matches(CommonRules.GstinPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.GstNo))
            .WithMessage("GST number must be 15 characters, e.g. 27AAAAA0000A1Z5.");
        RuleFor(x => x.MobileNo).Matches(CommonRules.MobilePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.MobileNo))
            .WithMessage("Mobile number must be 10 digits starting 6-9.");
        RuleFor(x => x.Email).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public class SaveWarehouseRequestValidator : AbstractValidator<SaveWarehouseRequest>
{
    public SaveWarehouseRequestValidator()
    {
        RuleFor(x => x.WarehouseName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleForEach(x => x.Bins).MaximumLength(100).WithMessage("A bin name is at most 100 characters.");
    }
}

public class SaveSupplierRequestValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator()
    {
        RuleFor(x => x.SupplierName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SupplierCode).MaximumLength(20);
        RuleFor(x => x.GstNumber).Matches(CommonRules.GstinPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.GstNumber))
            .WithMessage("GST number must be 15 characters, e.g. 27AAAAA0000A1Z5.");
        RuleFor(x => x.PanNumber).Matches(CommonRules.PanPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.PanNumber))
            .WithMessage("PAN must be 10 characters, e.g. AAAAA0000A.");
        RuleFor(x => x.BankIfsc).Matches(CommonRules.IfscPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.BankIfsc))
            .WithMessage("IFSC must be 11 characters, e.g. SBIN0001234.");
        RuleFor(x => x.Pincode).Matches(CommonRules.PincodePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Pincode))
            .WithMessage("Pincode must be 6 digits.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.PaymentTermDays).InclusiveBetween(0, 365);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0)
            .WithMessage("Enter the opening balance as a positive amount and pick DR or CR.");
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.City).MaximumLength(80);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FatherName).MaximumLength(150);
        RuleFor(x => x.CustomerCode).MaximumLength(20);
        RuleFor(x => x.Village).MaximumLength(100);
        RuleFor(x => x.Mobile).Matches(CommonRules.MobilePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Mobile))
            .WithMessage("Mobile must be 10 digits starting with 6, 7, 8 or 9.");
        RuleFor(x => x.AlternateMobile).Matches(CommonRules.MobilePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.AlternateMobile))
            .WithMessage("Alternate mobile must be 10 digits starting with 6, 7, 8 or 9.");
        RuleFor(x => x.GstNumber).Matches(CommonRules.GstinPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.GstNumber))
            .WithMessage("GST number must be 15 characters, e.g. 27AAAAA0000A1Z5.");
        RuleFor(x => x.Pincode).Matches(CommonRules.PincodePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Pincode))
            .WithMessage("Pincode must be 6 digits.");
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CreditDays).InclusiveBetween(0, 365);
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0)
            .WithMessage("Enter the opening balance as a positive amount and pick DR or CR.");
        // Credit terms without a way to chase the customer is how a shop's
        // receivables quietly become uncollectable.
        RuleFor(x => x.Mobile)
            .NotEmpty()
            .When(x => x.CreditLimit > 0)
            .WithMessage("A mobile number is required before a credit limit can be set.");
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public class SaveUnitRequestValidator : AbstractValidator<SaveUnitRequest>
{
    public SaveUnitRequestValidator()
    {
        RuleFor(x => x.UnitCode).NotEmpty().MaximumLength(10)
            .Matches("^[A-Za-z]+$").WithMessage("Unit code may contain letters only.");
        RuleFor(x => x.UnitName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(150);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public class SaveItemRequestValidator : AbstractValidator<SaveItemRequest>
{
    public SaveItemRequestValidator()
    {
        RuleFor(x => x.ItemName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ItemCode).MaximumLength(30);
        RuleFor(x => x.ShortName).MaximumLength(60);
        RuleFor(x => x.TechnicalName).MaximumLength(200);
        RuleFor(x => x.Brand).MaximumLength(100);
        RuleFor(x => x.ItemSubGroupId).GreaterThan(0).WithMessage("Select a itemSubGroup.");
        RuleFor(x => x.UnitId).GreaterThan(0).WithMessage("Select a unit.");
        RuleFor(x => x.GstSlabId).GreaterThan(0).WithMessage("Select a GST rate.");

        RuleFor(x => x.PackingSize).GreaterThan(0)
            .When(x => x.PackingSize.HasValue)
            .WithMessage("Packing size must be greater than zero.");

        // One rule per rate, not RuleForEach over an array: FluentValidation
        // cannot infer a property name from an array-construction expression
        // and throws at validation time. Naming each field also puts the error
        // on the right input in the form.
        RuleFor(x => x.PurchaseRate).GreaterThanOrEqualTo(0).WithMessage("Purchase rate cannot be negative.");
        RuleFor(x => x.SellingRate).GreaterThanOrEqualTo(0).WithMessage("Selling rate cannot be negative.");
        RuleFor(x => x.Mrp).GreaterThanOrEqualTo(0).WithMessage("MRP cannot be negative.");
        RuleFor(x => x.WholesaleRate).GreaterThanOrEqualTo(0).WithMessage("Wholesale rate cannot be negative.");
        RuleFor(x => x.DealerRate).GreaterThanOrEqualTo(0).WithMessage("Dealer rate cannot be negative.");
        RuleFor(x => x.MinSellingRate).GreaterThanOrEqualTo(0).WithMessage("Minimum selling rate cannot be negative.");

        RuleFor(x => x.MinStockLevel).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxStockLevel).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);

        // Expiry tracking is meaningless without a batch to hang the date on.
        RuleFor(x => x.IsExpiryTracked)
            .Must((request, expiry) => !expiry || request.IsBatchTracked)
            .WithMessage("Expiry tracking requires batch tracking to be enabled.");
    }
}
