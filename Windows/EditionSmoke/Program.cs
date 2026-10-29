using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

if (args.Length != 2) throw new ArgumentException("Pass GGML and Microsoft publish directories.");
Check(args[0], true);
Check(args[1], false);
Console.WriteLine("PASS: edition engine isolation, dependency isolation, bundled model checksum.");

static void Check(string directory, bool ggml)
{
    using var assembly = File.OpenRead(Path.Combine(directory, "Murmur.dll"));
    using var pe = new PEReader(assembly);
    var metadata = pe.GetMetadataReader();
    var names = metadata.TypeDefinitions.Select(h => metadata.GetString(metadata.GetTypeDefinition(h).Name)).ToHashSet();
    foreach (var (type, expected) in new[] { ("LocalSpeechEngine", ggml), ("OnlineSpeechEngine", !ggml), ("OnDeviceSpeechEngine", !ggml) })
        if (names.Contains(type) != expected) throw new Exception($"Unexpected engine {type} in {directory}");
    var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToHashSet();
    if (references.Contains("Whisper.net") != ggml || File.Exists(Path.Combine(directory, "Whisper.net.dll")) != ggml)
        throw new Exception("Whisper dependency does not match edition");
    var model = Path.Combine(directory, "Models", "speech-model.bin");
    if (File.Exists(model) != ggml) throw new Exception("Model presence does not match edition");
    if (ggml)
    {
        using var stream = File.OpenRead(model);
        if (Convert.ToHexString(SHA256.HashData(stream)) != "4BAF70DD0D7C4247BA2B81FAFD9C01005AC77C2F9EF064E00DCF195D0E2FDD2F")
            throw new Exception("Bundled model checksum mismatch");
    }
}
