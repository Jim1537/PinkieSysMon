namespace PinkieSysMon;

// Runtime-only projection. The shared model, IDs and validation have one
// implementation compiled into both Runtime and the independent Launcher.
internal static partial class FilesystemSourceContract
{
    public static IReadOnlyList<MetricDescriptor> DescriptorsFor(IEnumerable<FilesystemSourceConfig> sources)
    {
        var descriptors = new List<MetricDescriptor>();
        foreach (var source in sources)
        {
            descriptors.Add(new MetricDescriptor(MetricId(source.Id, Size), MetricValueKind.DataSize, MetricUnit.Bytes));
            descriptors.Add(new MetricDescriptor(MetricId(source.Id, Created), MetricValueKind.DateTime, MetricUnit.None));
            descriptors.Add(new MetricDescriptor(MetricId(source.Id, Modified), MetricValueKind.DateTime, MetricUnit.None));
            if (source.Kind == DirectoryKind)
            {
                descriptors.Add(new MetricDescriptor(MetricId(source.Id, Files), MetricValueKind.Number, MetricUnit.None));
                descriptors.Add(new MetricDescriptor(MetricId(source.Id, Directories), MetricValueKind.Number, MetricUnit.None));
            }
        }
        return descriptors;
    }

}
