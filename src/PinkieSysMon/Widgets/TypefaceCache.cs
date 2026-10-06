using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class TypefaceCache : IDisposable
{
    private readonly Dictionary<string, SKTypeface> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SKTypeface> _globalFonts = [];
    private readonly string _fontsDirectory;


    public TypefaceCache(string dashboardBaseDirectory, IEnumerable<CanonicalWidgetDefinition> widgets)
    {
        _fontsDirectory = GlobalAssetResolver.GetFontsRoot(dashboardBaseDirectory);
        LoadRequestedGlobalFonts(widgets);
    }

    public SKTypeface Get(TextPresentation presentation)
    {
        var weight = Math.Clamp(presentation.FontWeight, 1, 1000);
        var key = BuildKey(presentation.FontFamily, weight, presentation.Italic);
        if (_resolved.TryGetValue(key, out var cached))
            return cached;

        var requestedSlant = presentation.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

        var bundled = _globalFonts
            .Where(face => string.Equals(face.FamilyName, presentation.FontFamily, StringComparison.OrdinalIgnoreCase))
            .OrderBy(face => SlantPenalty(face.FontSlant, requestedSlant))
            .ThenBy(face => Math.Abs(face.FontWeight - weight))
            .FirstOrDefault();

        if (bundled is null)
        {
            throw new InvalidDataException(
                $"Bundled font family '{presentation.FontFamily}' was not found under '{_fontsDirectory}'. " +
                "PinkieSysMon does not use system-installed fonts.");
        }

        _resolved[key] = bundled;
        return bundled;
    }

    private void LoadRequestedGlobalFonts(IEnumerable<CanonicalWidgetDefinition> widgets)
    {
        var requests = widgets
            .SelectMany(GetFontRequests)
            .Distinct()
            .ToArray();

        if (requests.Length == 0 || !Directory.Exists(_fontsDirectory))
            return;

        var requestedFamilies = requests
            .Select(request => request.Family)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<SKTypeface>();
        try
        {
            foreach (var path in EnumerateFontFiles())
            {
                var typeface = SKTypeface.FromFile(path);
                if (typeface is null)
                    throw new InvalidDataException($"Global font file is not a valid TTF/OTF font: '{path}'.");

                if (requestedFamilies.Contains(typeface.FamilyName))
                    candidates.Add(typeface);
                else
                    typeface.Dispose();
            }

            var selected = new HashSet<SKTypeface>(ReferenceEqualityComparer.Instance);
            foreach (var request in requests)
            {
                var requestedSlant = request.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                var best = candidates
                    .Where(face => string.Equals(face.FamilyName, request.Family, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(face => SlantPenalty(face.FontSlant, requestedSlant))
                    .ThenBy(face => Math.Abs(face.FontWeight - request.Weight))
                    .FirstOrDefault();

                if (best is null)
                    continue;

                selected.Add(best);
                _resolved[BuildKey(request.Family, request.Weight, request.Italic)] = best;
            }

            foreach (var candidate in candidates)
            {
                if (selected.Contains(candidate))
                    _globalFonts.Add(candidate);
                else
                    candidate.Dispose();
            }
        }
        catch
        {
            foreach (var candidate in candidates)
                candidate.Dispose();
            _globalFonts.Clear();
            _resolved.Clear();
            throw;
        }
    }

    private static IEnumerable<FontRequest> GetFontRequests(CanonicalWidgetDefinition widget)
    {
        if (widget is CanonicalDashboard.ValueWidgetDefinition value)
        {
            var text = value.TextPresentation;
            if (!string.IsNullOrWhiteSpace(text.FontFamily))
            {
                yield return new FontRequest(
                    text.FontFamily.Trim(),
                    Math.Clamp(text.FontWeight, 1, 1000),
                    text.Italic);
            }

            yield break;
        }

        if (widget is not CanonicalDashboard.StateVisualWidgetDefinition stateWidget)
            yield break;

        foreach (var profile in stateWidget.Profiles.Values)
        {
            if (!CanonicalDashboard.StateContentType.IsValue(profile.ContentType) ||
                string.IsNullOrWhiteSpace(profile.TextPresentation.FontFamily))
            {
                continue;
            }

            yield return new FontRequest(
                profile.TextPresentation.FontFamily.Trim(),
                Math.Clamp(profile.TextPresentation.FontWeight, 1, 1000),
                profile.TextPresentation.Italic);
        }
    }

    private IEnumerable<string> EnumerateFontFiles() =>
        Directory
            .EnumerateFiles(_fontsDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                Path.GetExtension(path).Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static string BuildKey(string family, int weight, bool italic) =>
        $"{family}|{weight}|{italic}";

    private static int SlantPenalty(SKFontStyleSlant actual, SKFontStyleSlant requested)
    {
        if (actual == requested)
            return 0;

        var actualItalic = actual != SKFontStyleSlant.Upright;
        var requestedItalic = requested != SKFontStyleSlant.Upright;
        return actualItalic == requestedItalic ? 1 : 10000;
    }

    public void Dispose()
    {
        foreach (var typeface in _resolved.Values
                     .Concat(_globalFonts)
                     .Distinct<SKTypeface>(ReferenceEqualityComparer.Instance))
        {
            typeface.Dispose();
        }

        _resolved.Clear();
        _globalFonts.Clear();
    }

    private readonly record struct FontRequest(string Family, int Weight, bool Italic);
}
