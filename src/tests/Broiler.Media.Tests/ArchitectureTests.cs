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
            // §6.6: the Media Foundation backend borrows the HWND presentation target owned
            // by Broiler.Graphics.Windows — the one approved Graphics edge.
            ["Broiler.Media.Video.MediaFoundation/Broiler.Media.Video.MediaFoundation.csproj"] =
                [
                    "../Broiler.Media.Video/Broiler.Media.Video.csproj",
                    "../../Broiler.Graphics/Broiler.Graphics.Windows/Broiler.Graphics.Direct2D.csproj",
                ],
            ["Broiler.Media.Image/Broiler.Media.Image.csproj"] =
                ["../Broiler.Media/Broiler.Media.csproj"],
            ["Broiler.Media.Image.Managed/Broiler.Media.Image.Managed.csproj"] =
                ["../Broiler.Media.Image/Broiler.Media.Image.csproj"],
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
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Broiler.Media component root.");
    }
}

