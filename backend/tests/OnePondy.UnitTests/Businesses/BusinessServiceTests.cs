using System.Text;
using OnePondy.Domain.Businesses;

namespace OnePondy.UnitTests.Businesses;

public class BusinessServiceTests
{
    private readonly BusinessHarness _harness = new();

    [Fact]
    public async Task Applying_creates_a_pending_business_that_is_not_yet_active()
    {
        var result = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(BusinessApprovalStatus.Pending, result.Value.ApprovalStatus);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task A_restaurant_cannot_be_approved_without_an_fssai_number()
    {
        var applied = await _harness.Businesses.ApplyAsync(
            _harness.Customer,
            BusinessHarness.Input(businessType: BusinessType.Restaurant),
            default);

        var result = await _harness.Businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, _harness.Administrator, default);

        Assert.Equal("business_cannot_approve", result.Error!.Code);
    }

    [Fact]
    public async Task A_restaurant_can_be_approved_once_it_has_an_fssai_number()
    {
        var applied = await _harness.Businesses.ApplyAsync(
            _harness.Customer,
            BusinessHarness.Input(businessType: BusinessType.Restaurant, fssaiLicenseNumber: "12345678901234"),
            default);

        var result = await _harness.Businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, _harness.Administrator, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Approving_a_non_restaurant_activates_it_immediately()
    {
        var applied = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);

        var result = await _harness.Businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, _harness.Administrator, default);

        Assert.Equal(BusinessApprovalStatus.Approved, result.Value!.ApprovalStatus);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_deactivates_the_business()
    {
        var applied = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);

        var blank = await _harness.Businesses.RejectAsync(applied.Value!.Id, applied.Value.Version, null, _harness.Administrator, default);
        Assert.Equal("validation_failed", blank.Error!.Code);

        var result = await _harness.Businesses.RejectAsync(
            applied.Value.Id,
            applied.Value.Version,
            "Missing documents",
            _harness.Administrator,
            default);

        Assert.Equal(BusinessApprovalStatus.Rejected, result.Value!.ApprovalStatus);
        Assert.False(result.Value.IsActive);
        Assert.Equal("Missing documents", result.Value.ReviewNote);
    }

    [Fact]
    public async Task Editing_a_rejected_application_resubmits_it_for_review()
    {
        var applied = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);
        var rejected = await _harness.Businesses.RejectAsync(
            applied.Value!.Id,
            applied.Value.Version,
            "Missing documents",
            _harness.Administrator,
            default);

        var updated = await _harness.Businesses.UpdateAsync(
            rejected.Value!.Id,
            rejected.Value.Version,
            _harness.Customer,
            BusinessHarness.Input(name: "Pondy Paddle Tours (updated)"),
            default);

        Assert.Equal(BusinessApprovalStatus.Pending, updated.Value!.ApprovalStatus);
        Assert.Null(updated.Value.ReviewNote);
    }

    [Fact]
    public async Task An_approved_business_cannot_be_edited_by_its_owner()
    {
        var applied = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);
        var approved = await _harness.Businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, _harness.Administrator, default);

        var result = await _harness.Businesses.UpdateAsync(
            approved.Value!.Id,
            approved.Value.Version,
            _harness.Customer,
            BusinessHarness.Input(),
            default);

        Assert.Equal("business_locked", result.Error!.Code);
    }

    [Fact]
    public async Task Suspending_and_reinstating_only_works_from_the_matching_status()
    {
        var applied = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);
        var approved = await _harness.Businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, _harness.Administrator, default);

        var cannotReinstate = await _harness.Businesses.ReinstateAsync(approved.Value!.Id, approved.Value.Version, _harness.Administrator, default);
        Assert.Equal("business_invalid_transition", cannotReinstate.Error!.Code);

        var suspended = await _harness.Businesses.SuspendAsync(
            approved.Value.Id,
            approved.Value.Version,
            "Multiple complaints",
            _harness.Administrator,
            default);
        Assert.Equal(BusinessApprovalStatus.Suspended, suspended.Value!.ApprovalStatus);
        Assert.False(suspended.Value.IsActive);

        var reinstated = await _harness.Businesses.ReinstateAsync(suspended.Value.Id, suspended.Value.Version, _harness.Administrator, default);
        Assert.Equal(BusinessApprovalStatus.Approved, reinstated.Value!.ApprovalStatus);
        Assert.True(reinstated.Value.IsActive);
    }

    [Fact]
    public async Task Only_the_owner_can_see_or_edit_their_own_business()
    {
        var applied = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);
        var someoneElse = Guid.NewGuid();

        var getResult = await _harness.Businesses.GetMineAsync(applied.Value!.Id, someoneElse, default);
        Assert.Equal("business_not_found", getResult.Error!.Code);

        var updateResult = await _harness.Businesses.UpdateAsync(
            applied.Value.Id,
            applied.Value.Version,
            someoneElse,
            BusinessHarness.Input(),
            default);
        Assert.Equal("business_not_found", updateResult.Error!.Code);
    }

    [Fact]
    public async Task A_document_can_be_uploaded_downloaded_by_its_owner_and_removed()
    {
        var applied = await _harness.Businesses.ApplyAsync(_harness.Customer, BusinessHarness.Input(), default);
        var bytes = Encoding.UTF8.GetBytes("fake gstin certificate");

        var uploaded = await _harness.Businesses.AddDocumentAsync(
            applied.Value!.Id,
            _harness.Customer,
            BusinessDocumentKind.Gstin,
            bytes,
            "application/pdf",
            default);
        Assert.True(uploaded.IsSuccess);

        var downloaded = await _harness.Businesses.GetDocumentAsync(applied.Value.Id, uploaded.Value!.Id, _harness.Customer, default);
        Assert.Equal(bytes, downloaded.Value.Content);

        var someoneElse = Guid.NewGuid();
        var forbidden = await _harness.Businesses.GetDocumentAsync(applied.Value.Id, uploaded.Value.Id, someoneElse, default);
        Assert.Equal("business_not_found", forbidden.Error!.Code);

        // A staff request (no owner check) can also read it.
        var staffRead = await _harness.Businesses.GetDocumentAsync(applied.Value.Id, uploaded.Value.Id, requireOwnerUserId: null, default);
        Assert.True(staffRead.IsSuccess);

        await _harness.Businesses.RemoveDocumentAsync(applied.Value.Id, uploaded.Value.Id, _harness.Customer, default);
        var afterRemoval = await _harness.Businesses.GetDocumentAsync(applied.Value.Id, uploaded.Value.Id, _harness.Customer, default);
        Assert.Equal("business_document_not_found", afterRemoval.Error!.Code);
    }
}
