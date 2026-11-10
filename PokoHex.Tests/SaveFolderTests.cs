using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using PokoHex.Core;
using Xunit;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PokoHex.Tests;

public static class SaveFolderTests
{
    [Fact]
    public static void PreserveImportedFiles()
    {
        var source = GetFiles();
        var save = SaveFolder.ReadFiles(source);
        var output = save.ExportFiles();
        foreach (var (path, data) in source)
            Assert.Equal(data, output[path.Replace('/', Path.DirectorySeparatorChar)]);
        source["S00/opaque.dat"][0] = 99;
        output[Path.Combine("S00", "opaque.dat")][0] = 88;
        Assert.Equal(new byte[] { 1, 2, 3 }, save.ExportFiles()[Path.Combine("S00", "opaque.dat")]);
        save.Common.PlayerName = "Web";
        var reopened = SaveFolder.ReadFiles(save.ExportFiles());
        Assert.Equal("Web", reopened.Common.PlayerName);
        Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ExportFiles()[Path.Combine("S00", "opaque.dat")]);
    }

    [Fact]
    public static void RejectInvalidImportedPaths()
    {
        foreach (var path in new[] { "../file", "/file", "G00/../file", "C:/file", "G00//file", "G00/./file" })
        {
            var files = GetFiles();
            files.Add(path, []);
            Assert.Throws<InvalidDataException>(() => SaveFolder.ReadFiles(files));
        }
        var duplicate = GetFiles();
        duplicate.Add("g00\\cmndat.bin", duplicate["G00/CMNDAT.BIN"]);
        Assert.Throws<InvalidDataException>(() => SaveFolder.ReadFiles(duplicate));
        var missing = GetFiles();
        missing.Remove("S00/opaque.dat");
        Assert.Throws<InvalidDataException>(() => SaveFolder.ReadFiles(missing));
    }

    [Fact]
    public static void EditLifeCoins()
    {
        var files = GetFiles(SaveLayout.Version381);
        var save = SaveFolder.ReadFiles(files);
        Assert.True(save.Common.HasLifeCoins);
        Assert.Equal(0u, save.Common.LifeCoins);
        save.Common.LifeCoins = 0;
        Assert.False(save.IsEdited);
        Assert.Throws<ArgumentException>(() => save.Common.LifeCoins = SaveFile.MaxLifeCoins + 1);
        save.Common.LifeCoins = 710;
        Assert.Equal(710u, ReadUInt32LittleEndian(save.Common.Data.AsSpan(SaveLayout.Version381.LifeCoinsOffset)));
        save.Common.LifeCoins = SaveFile.MaxLifeCoins;
        var reopened = SaveFolder.ReadFiles(save.ExportFiles());
        Assert.Equal(SaveFile.MaxLifeCoins, reopened.Common.LifeCoins);
        var offset = SaveLayout.Version381.LifeCoinsOffset;
        Assert.All(Enumerable.Range(0, save.Common.Data.Length).Where(i => save.Common.Data[i] != 0),
            i => Assert.InRange(i, offset, offset + 3));

        var old = SaveFolder.ReadFiles(GetFiles());
        Assert.False(old.Common.HasLifeCoins);
        Assert.Throws<InvalidOperationException>(() => old.Common.LifeCoins);
    }

    private static Dictionary<string, byte[]> GetFiles(SaveLayout? layout = null)
    {
        layout ??= SaveLayout.Version70;
        using var output = new MemoryStream();
        output.Write(new byte[layout.HeaderSize]);
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, true))
            zlib.Write(new byte[layout.DataSize]);
        var common = output.ToArray();
        WriteUInt32LittleEndian(common, 0x31227687);
        WriteUInt32LittleEndian(common.AsSpan(4), layout.Version);
        WriteUInt32LittleEndian(common.AsSpan(8), 0x40001);
        WriteInt32LittleEndian(common.AsSpan(0x10), common.Length);
        return new Dictionary<string, byte[]>
        {
            ["G00/CMNDAT.BIN"] = common,
            ["S00/opaque.dat"] = [1, 2, 3],
        };
    }
}
