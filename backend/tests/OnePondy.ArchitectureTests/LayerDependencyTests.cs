using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace OnePondy.ArchitectureTests;

/// <summary>
/// Enforces the layer direction in spec §9: Domain depends on nothing, Application on Domain,
/// and Infrastructure, Api and Worker on Application.
/// </summary>
public class LayerDependencyTests
{
    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "OnePondy.Domain", [] },
        { "OnePondy.Application", ["OnePondy.Domain"] },
        { "OnePondy.Infrastructure", ["OnePondy.Application"] },
        { "OnePondy.Api", ["OnePondy.Application", "OnePondy.Infrastructure"] },
        { "OnePondy.Worker", ["OnePondy.Application", "OnePondy.Infrastructure"] },
    };

    public static TheoryData<string, string[]> ForbiddenTypeDependencies => new()
    {
        { "OnePondy.Domain", ["OnePondy.Application", "OnePondy.Infrastructure", "OnePondy.Api", "OnePondy.Worker"] },
        { "OnePondy.Application", ["OnePondy.Infrastructure", "OnePondy.Api", "OnePondy.Worker"] },
        { "OnePondy.Infrastructure", ["OnePondy.Api", "OnePondy.Worker"] },
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void Project_references_follow_the_layer_direction(string project, string[] allowed)
    {
        var references = ReadProjectReferences(project);

        Assert.Subset(allowed.ToHashSet(), references);
    }

    [Theory]
    [MemberData(nameof(ForbiddenTypeDependencies))]
    public void Types_do_not_depend_on_outer_layers(string layer, string[] forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(layer))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{layer} depends on: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    private static HashSet<string> ReadProjectReferences(string project)
    {
        var projectFile = Path.Combine(FindBackendRoot(), "src", project, $"{project}.csproj");

        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value.Replace('\\', '/'))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet();
    }

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OnePondy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("OnePondy.sln not found above the test output.");
    }
}
