using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Broiler.Media.Tests;

internal static class ArchitectureTests
{
    public static void Register(ICollection<(string Name, Func<ValueTask> Body)> tests)
    {
        tests.Add(("Media projects have no third-party package references", NoPackageReferences));
        tests.Add(("Runtime project references match the Phase 1 allowlist", RuntimeReferenceAllowlist));
        tests.Add(("Abstractions do not reference Graphics, HTML, or Media Foundation", AbstractionsStayNeutral));
        tests.Add(("Nothing in the component references Broiler.Graphics", ComponentNeverReferencesGraphics));
        tests.Add(("Runtime sources avoid hidden module-initializer registration", NoModuleInitializers));
        tests.Add(("Shared Media has no untyped object Decode method", NoUntypedSharedDecode));
    }

    private static ValueTask NoPackageReferences()
    {
        string root = FindMediaRoot();
        foreach (string project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(project);
            Assert.DoesNotContain("<PackageReference", text, project);
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask RuntimeReferenceAllowlist()
    {
        string root = FindMediaRoot();
        var expected = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Broiler.Media/Broiler.Media.csproj"] = [],
            ["Broiler.Media.Audio/Broiler.Media.Audio.csproj"] =
                ["../Broiler.Media/Broiler.Media.csproj"],
            ["Broiler.Media.Audio.Managed/Broiler.Media.Audio.Managed.csproj"] =
                ["../Broiler.Media.Audio/Broiler.Media.Audio.csproj"],
            ["Broiler.Media.Video/Broiler.Media.Video.csproj"] =
                ["../Broiler.Media/Broiler.Media.csproj"],
            // §6.6: the Windows presentation-target contract the Media Foundation backend
            // borrows. Contracts only — it must never reference an implementation.
            ["Broiler.Media.Video.Windows/Broiler.Media.Video.Windows.csproj"] =
                ["../Broiler.Media.Video/Broiler.Media.Video.csproj"],
            // §6.6: the Media Foundation backend borrows an HWND presentation target through
            // the IHwndVideoOutput contract above. It used to reference
            // Broiler.Graphics.Windows for the concrete target type, which closed a
            // Media → Graphics → Media cycle; ADR 0006 inverted it. No Graphics edge remains.
            ["Broiler.Media.Video.MediaFoundation/Broiler.Media.Video.MediaFoundation.csproj"] =
                [
                    "../Broiler.Media.Video/Broiler.Media.Video.csproj",
                    "../Broiler.Media.Video.Windows/Broiler.Media.Video.Windows.csproj",
                ],
            ["Broiler.Media.Image/Broiler.Media.Image.csproj"] =
                ["../Broiler.Media/Broiler.Media.csproj"],
            ["Broiler.Media.Image.Managed/Broiler.Media.Image.Managed.csproj"] =
                ["../Broiler.Media.Image/Broiler.Media.Image.csproj"],
            // The meta-package carries the cross-platform stack only; platform-native
            // backends (MediaFoundation) stay separate packages.
            ["Broiler.Media.All/Broiler.Media.All.csproj"] =
                [
                    "../Broiler.Media/Broiler.Media.csproj",
                    "../Broiler.Media.Audio/Broiler.Media.Audio.csproj",
                    "../Broiler.Media.Audio.Managed/Broiler.Media.Audio.Managed.csproj",
                    "../Broiler.Media.Video/Broiler.Media.Video.csproj",
                    "../Broiler.Media.Image/Broiler.Media.Image.csproj",
                    "../Broiler.Media.Image.Managed/Broiler.Media.Image.Managed.csproj",
                ],
        };

        foreach ((string relativeProject, string[] expectedReferences) in expected)
        {
            string projectPath = Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar));
            string[] actual = ReadProjectReferences(projectPath)
                .Select(NormalizeProjectReference)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] wanted = expectedReferences
                .Select(NormalizeProjectReference)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.SequenceEqual(wanted, actual, relativeProject);
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask AbstractionsStayNeutral()
    {
        string root = FindMediaRoot();
        string[] abstractionProjects =
        [
            "Broiler.Media",
            "Broiler.Media.Audio",
            "Broiler.Media.Video",
            "Broiler.Media.Image",
        ];

        foreach (string projectName in abstractionProjects)
        {
            string projectRoot = Path.Combine(root, projectName);
            foreach (string file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                Assert.DoesNotContain("Broiler.Graphics", text, file);
                Assert.DoesNotContain("Broiler.HTML", text, file);
                Assert.DoesNotContain("MediaFoundation", text, file);
                Assert.DoesNotContain("IMFMediaEngine", text, file);
                Assert.DoesNotContain("HWND", text, file);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Broiler.Media is a leaf: it depends on no other Broiler component. This is the guard on
    /// ADR 0006. Until then, Broiler.Media.Video.MediaFoundation referenced
    /// Broiler.Graphics.Windows for the concrete HWND target type while Broiler.Graphics
    /// referenced Broiler.Media.Image — a component-level cycle that forced each repository to
    /// carry a submodule checkout of the other, and made a single build compile two copies of
    /// Broiler.Media.Image from two different source trees.
    /// </summary>
    private static ValueTask ComponentNeverReferencesGraphics()
    {
        string root = FindMediaRoot();

        // Every project in the component, tests included — a project reference is the actual
        // dependency, and a test project reaching for Graphics rebuilds the cycle just as
        // surely as a runtime one. (Test *sources* may name the string in assertions like
        // this one, so only .csproj files are scanned here.)
        foreach (string project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            Assert.DoesNotContain("Broiler.Graphics", File.ReadAllText(project), project);

        foreach (string file in RuntimeSourceFiles(root))
            Assert.DoesNotContain("Broiler.Graphics", File.ReadAllText(file), file);

        // A source reference is only half of it: the cycle was also physical, as a
        // Broiler.Graphics submodule checked out inside this component. Neither may return.
        string componentRoot = Directory.GetParent(root)!.FullName;
        string modules = Path.Combine(componentRoot, ".gitmodules");
        if (File.Exists(modules))
            Assert.DoesNotContain("Broiler.Graphics", File.ReadAllText(modules), modules);

        Assert.False(
            Directory.Exists(Path.Combine(componentRoot, "Broiler.Graphics")),
            "Broiler.Media must not carry a Broiler.Graphics checkout.");

        return ValueTask.CompletedTask;
    }

    private static ValueTask NoModuleInitializers()
    {
        string root = FindMediaRoot();
        foreach (string file in RuntimeSourceFiles(root))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("ModuleInitializer", text, file);
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask NoUntypedSharedDecode()
    {
        string sharedRoot = Path.Combine(FindMediaRoot(), "Broiler.Media");
        foreach (string file in Directory.EnumerateFiles(sharedRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("object Decode", text, file);
            Assert.DoesNotContain("object? Decode", text, file);
        }

        return ValueTask.CompletedTask;
    }

    private static IEnumerable<string> RuntimeSourceFiles(string root)
    {
        string[] runtimeFolders =
        [
            "Broiler.Media",
            "Broiler.Media.Audio",
            "Broiler.Media.Audio.Managed",
            "Broiler.Media.Video",
            "Broiler.Media.Video.Windows",
            "Broiler.Media.Video.MediaFoundation",
            "Broiler.Media.Image",
            "Broiler.Media.Image.Managed",
        ];

        foreach (string folder in runtimeFolders)
        {
            string fullPath = Path.Combine(root, folder);
            foreach (string file in Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories))
                yield return file;
        }
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
    }

    private static string NormalizeProjectReference(string reference) =>
        reference.Replace('\\', '/');

    private static string FindMediaRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broiler.Media.slnx")))
                return Path.Combine(directory.FullName, "src");

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Broiler.Media component root.");
    }
}

