using TgaViewer.TestSupport;

// Writes the sample catalogue (see TgaSamples) for manual testing, or with --reference the inputs for
// the Pillow comparison (see TgaReferenceSamples and tools/reference/README.md).
// Usage: dotnet run --project tools/TgaViewer.SampleGenerator -- [output folder] [--large | --reference]
string folder = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine("samples", "generated");
Directory.CreateDirectory(folder);

foreach (var (name, bytes) in args.Contains("--reference") ? TgaReferenceSamples.All() : TgaSamples.All())
{
    File.WriteAllBytes(Path.Combine(folder, name), bytes);
    Console.WriteLine($"{bytes.Length,12:N0}  {name}");
}

if (args.Contains("--large"))
{
    foreach (var (name, bytes) in new[]
    {
        ("large 8192 x 8192 RLE 32-bit.tga", LargeTga.Rle32(8192, 8192)),
        ("large 8192 x 8192 uncompressed 32-bit.tga", LargeTga.Uncompressed32(8192, 8192)),
    })
    {
        File.WriteAllBytes(Path.Combine(folder, name), bytes);
        Console.WriteLine($"{bytes.Length,12:N0}  {name}");
    }
}

Console.WriteLine($"Samples written to {Path.GetFullPath(folder)}");
