using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue70ImageImportTests
{
    public static void DeclinePreservesWidgetConfigurations()
    {
        InSandbox((source, destination) =>
        {
            var image = new C.ImageWidgetDefinition
            {
                Id = "image", Width = 20, Height = 20, Opacity = 0.3f,
                Asset = new C.ImageAssetPresentationDefinition
                {
                    Source = "images/art.png", SourceType = C.ImageAssetSourceType.File,
                    Fit = StateVisualProfileContract.FitCover
                }
            };
            var bar = new C.BarWidgetDefinition
            {
                Id = "bar", Width = 30, Height = 20,
                ContentMode = BarImageContract.ContentModeImage,
                Image = new C.BarImagePresentationDefinition { Source = "images/art.png" }
            };
            var binary = new C.BinaryWidgetDefinition
            {
                Id = "state", Width = 16, Height = 16,
                Profiles = new Dictionary<string, C.StateVisualProfileDefinition>
                {
                    ["true"] = new C.StateVisualProfileDefinition
                    {
                        ContentType = C.StateContentType.Image,
                        Asset = new C.ImageAssetPresentationDefinition
                        {
                            SourceType = C.ImageAssetSourceType.File, Source = "images/art.png"
                        }
                    },
                    ["false"] = new C.StateVisualProfileDefinition
                    {
                        ContentType = C.StateContentType.Image,
                        Asset = new C.ImageAssetPresentationDefinition
                        {
                            SourceType = C.ImageAssetSourceType.Icon, Source = "lucide:cpu"
                        }
                    }
                }
            };
            var created = new List<string>();
            var asked = 0;
            var result = EditorForm.ResolveClipboardImages(
                [image, bar, binary], source, destination, created,
                count => { asked = count; return DialogResult.No; },
                _ => throw new InvalidOperationException("No collision prompt expected."));
            if (!result || asked != 1 || created.Count != 0 ||
                image.Asset.Source is not null || bar.Image.Source is not null ||
                binary.Profiles["true"].Asset.Source is not null ||
                binary.Profiles["false"].Asset.Source != "lucide:cpu" ||
                image.Opacity != 0.3f || image.Asset.Fit != StateVisualProfileContract.FitCover ||
                bar.ContentMode != BarImageContract.ContentModeImage)
                throw new InvalidOperationException("Declined import must only clear local image Sources.");
        });
    }

    public static void ImportAndCollisionChoices()
    {
        InSandbox((source, destination) =>
        {
            Save(source, "images/panel.png", "source-bytes");
            Save(destination, "images/panel.png", "destination-bytes");
            var first = Image("images/panel.png");
            var second = Image("images/panel.png");
            var created = new List<string>();
            var result = EditorForm.ResolveClipboardImages(
                [first, second], source, destination, created,
                _ => DialogResult.Yes, _ => DialogResult.Yes);
            if (!result || first.Asset.Source != "images/panel-1.png" ||
                second.Asset.Source != first.Asset.Source || created.Count != 1 ||
                File.ReadAllText(Path.Combine(destination, first.Asset.Source)) != "source-bytes" ||
                File.ReadAllText(Path.Combine(destination, "images", "panel.png")) != "destination-bytes")
                throw new InvalidOperationException("Conflicting source must get unique name without overwrite.");

            var reused = Image("images/panel.png");
            var reusedCreated = new List<string>();
            result = EditorForm.ResolveClipboardImages(
                [reused], source, destination, reusedCreated,
                _ => DialogResult.Yes, _ => DialogResult.No);
            if (!result || reused.Asset.Source != "images/panel.png" || reusedCreated.Count != 0)
                throw new InvalidOperationException("Use-existing image collision decision failed.");

            Save(source, "images/same.png", "identical");
            Save(destination, "images/same.png", "identical");
            var identical = Image("images/same.png");
            var sameCreated = new List<string>();
            result = EditorForm.ResolveClipboardImages(
                [identical], source, destination, sameCreated,
                _ => DialogResult.Yes,
                _ => throw new InvalidOperationException("Identical bytes require no collision dialog."));
            if (!result || identical.Asset.Source != "images/same.png" || sameCreated.Count != 0)
                throw new InvalidOperationException("Identical image must reuse destination bytes.");
        });
    }

    public static void CancelAndInvalidPaths()
    {
        InSandbox((source, destination) =>
        {
            Save(source, "images/panel.png", "source");
            Save(destination, "images/panel.png", "destination");
            var widget = Image("images/panel.png");
            var created = new List<string>();
            var accepted = EditorForm.ResolveClipboardImages(
                [widget], source, destination, created,
                _ => DialogResult.Cancel, _ => throw new InvalidOperationException());
            if (accepted || widget.Asset.Source != "images/panel.png" || created.Count != 0)
                throw new InvalidOperationException("Cancelling import must not mutate the source.");

            accepted = EditorForm.ResolveClipboardImages(
                [widget], source, destination, created,
                _ => DialogResult.Yes, _ => DialogResult.Cancel);
            if (accepted || created.Count != 0 ||
                File.ReadAllText(Path.Combine(destination, "images", "panel.png")) != "destination")
                throw new InvalidOperationException("Cancelling a name conflict must not overwrite assets.");

            foreach (var invalid in new[] { "../outside.png", "images/missing.png" })
            {
                var item = Image(invalid);
                var rejected = false;
                try
                {
                    _ = EditorForm.ResolveClipboardImages(
                        [item], source, destination, created,
                        _ => DialogResult.Yes, _ => DialogResult.Cancel);
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
                {
                    rejected = true;
                }
                if (!rejected || created.Count != 0)
                    throw new InvalidOperationException("Missing/escaping source image must be rejected.");
            }
        });
    }

    private static C.ImageWidgetDefinition Image(string source) => new()
    {
        Id = "image", Width = 16, Height = 16,
        Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.File, Source = source }
    };

    private static void Save(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void InSandbox(Action<string, string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "psm-images-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "dashboards", "source");
        var destination = Path.Combine(root, "dashboards", "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try { body(source, destination); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
