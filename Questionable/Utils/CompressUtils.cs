using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ECommons.DalamudServices;
using Newtonsoft.Json;

namespace Questionable.Utils;
internal static class CompressUtils
{
    // https://stackoverflow.com/questions/25134897/gzip-compression-and-decompression-in-c-sharp
    public static object? DecompressObject(string input)
    {
        return JsonConvert.DeserializeObject(Decompress(input));
    }
    public static object? CompressObject(object input)
    {
        return Compress(JsonConvert.SerializeObject(input));
    }

    public static string Decompress(string input)
    {
        byte[] compressed = Convert.FromBase64String(input);
        byte[] decompressed = Decompress(compressed);
        string output = Encoding.UTF8.GetString(decompressed);
        Svc.Log.Debug($"decompressed {input.Length} to {output.Length}");
        return output;
    }

    public static string Compress(string input)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(input);
        byte[] compressed = Compress(encoded);
        string output = Convert.ToBase64String(compressed);
        Svc.Log.Debug($"compressed {input.Length} to {output.Length}");
        return output;
    }

    public static byte[] Decompress(byte[] input)
    {
        using var source = new MemoryStream(input);
        using var result = new MemoryStream();
        using (var Decompress = new GZipStream(source, CompressionMode.Decompress))
        {
            Decompress.CopyTo(result);
        }

        return result.ToArray();
    }

    public static byte[] Compress(byte[] input)
    {
        using var source = new MemoryStream(input);
        using var result = new MemoryStream();
        using (var Compress = new GZipStream(result, CompressionMode.Compress))
        {
            source.CopyTo(Compress);
        }

        return result.ToArray();
    }
}
