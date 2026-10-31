using System.Reflection;

namespace PinkieSysMon.RegressionTests;

internal static class RegressionRegistryContractTests
{
    public static void AllRunnableCasesAreRegistered(
        IReadOnlyList<(string Group, (string Name, Action Run)[] Cases)> groups)
    {
        ValidateMetadata(groups);

        var registrations = groups.SelectMany(group => group.Cases)
            .Select(test => test.Run.Method)
            .ToHashSet();

        // Parameterless public void methods on static *Tests classes are
        // independently runnable checks. Async Task checks use explicit
        // synchronous adapters and are outside this reflection convention.
        var unregistered = typeof(RegressionRegistryContractTests).Assembly
            .GetTypes()
            .Where(type => type.IsAbstract && type.IsSealed &&
                           type.Name.EndsWith("Tests", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(void) &&
                             method.GetParameters().Length == 0 &&
                             !method.ContainsGenericParameters &&
                             !registrations.Contains(method))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (unregistered.Length != 0)
        {
            throw new InvalidOperationException(
                "Unregistered regression methods: " + string.Join(", ", unregistered));
        }
    }

    public static void MalformedRegistryMetadataIsRejected()
    {
        static (string Name, Action Run) Case(string name) => (name, () => { });
        static void ExpectInvalid(
            (string Group, (string Name, Action Run)[] Cases)[] input)
        {
            try
            {
                ValidateMetadata(input);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            throw new InvalidOperationException(
                "Invalid registration metadata was accepted.");
        }

        ValidateMetadata(
        [
            ("Infrastructure / Regression", [Case("independent check")])
        ]);
        ExpectInvalid(
        [
            ("Infrastructure / Regression",
                [Case("duplicated"), Case("duplicated")])
        ]);
        ExpectInvalid(
        [
            ("Infrastructure / Regression", [Case("one")]),
            ("Infrastructure / Regression", [Case("two")])
        ]);
        ExpectInvalid(
        [
            ("Infrastructure / Regression", [Case("")])
        ]);
        ExpectInvalid(
        [
            ("Infrastructure / Regression", [])
        ]);
        ExpectInvalid(
        [
            ("Infrastructure / A group name longer than the console column", [Case("one")])
        ]);
    }

    private static void ValidateMetadata(
        IEnumerable<(string Group, (string Name, Action Run)[] Cases)> groups)
    {
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Group) ||
                group.Group.Length > 34 ||
                !seenGroups.Add(group.Group) ||
                group.Cases is null || group.Cases.Length == 0)
            {
                throw new InvalidOperationException(
                    "Invalid, oversized, duplicated, or empty regression group.");
            }

            foreach (var test in group.Cases)
            {
                if (string.IsNullOrWhiteSpace(test.Name) ||
                    test.Run is null ||
                    !seenNames.Add(test.Name))
                {
                    throw new InvalidOperationException(
                        "Invalid or duplicated regression case registration.");
                }

                count++;
            }
        }

        if (count == 0)
        {
            throw new InvalidOperationException("No regression cases registered.");
        }
    }
}
