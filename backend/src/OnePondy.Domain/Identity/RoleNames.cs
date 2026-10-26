namespace OnePondy.Domain.Identity;

/// <summary>Account roles. Staff roles open the web console (spec §10, Phase 3).</summary>
public static class RoleNames
{
    public const string Customer = "customer";
    public const string Administrator = "administrator";
    public const string ContentEditor = "content_editor";
    public const string SupportAgent = "support_agent";

    public static readonly IReadOnlyList<string> Staff = [Administrator, ContentEditor, SupportAgent];

    public static bool IsStaff(string role) => Staff.Contains(role);
}
