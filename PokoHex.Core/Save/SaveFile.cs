using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PokoHex.Core;

public sealed class SaveFile
{
    private const uint Magic = 0x31227687;
    public const uint MaxLifeCoins = 999_999;
    private static readonly UTF8Encoding Encoding = new(false, true);
    private readonly byte[] Original;
    private readonly byte[] OriginalData;
    public byte[] Data { get; }
    public SaveLayout Layout { get; }
    public Item[] Inventory { get; }
    public Villager[] Villagers { get; }
    public PlayerAbilities? Abilities { get; }
    public FunctionFlags? Flags { get; }
    public bool IsEdited => !Data.AsSpan().SequenceEqual(OriginalData);

    private SaveFile(byte[] original, byte[] data, SaveLayout layout)
    {
        Original = original;
        OriginalData = (byte[])data.Clone();
        Data = data;
        Layout = layout;
        Inventory = Enumerable.Range(0, layout.InventoryCount)
            .Select(i => new Item(Data.AsMemory(layout.InventoryOffset + (i * Item.SIZE), Item.SIZE))).ToArray();
        Villagers = Enumerable.Range(0, layout.VillagerCount)
            .Select(i => new Villager(i, Data.AsMemory(i * Villager.SIZE, Villager.SIZE),
                Data.AsMemory(layout.VillagerHomeOffset + (i * VillagerHome.SIZE), VillagerHome.SIZE))).ToArray();
        if (layout.FunctionFlagsOffset != 0)
            Flags = new FunctionFlags(Data.AsMemory(layout.FunctionFlagsOffset, FunctionFlags.SIZE));
        if (layout.PlayerAbilitiesOffset != 0)
            Abilities = new PlayerAbilities(Data.AsMemory(layout.PlayerAbilitiesOffset, PlayerAbilities.SIZE),
                Data.AsMemory(layout.FunctionFlagsOffset, FunctionFlags.SIZE));
    }

    public string PlayerName
    {
        get => ReadName(GetNameOffset());
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
                throw new ArgumentException("Enter a name without control characters.");
            var bytes = Encoding.GetBytes(value);
            if (bytes.Length >= Layout.PlayerNameSize)
                throw new ArgumentException($"The name must fit in {Layout.PlayerNameSize - 1} UTF-8 bytes.");

            var old = PlayerName;
            for (int i = 0; i < Layout.PlayerNameCount; i++)
            {
                var offset = Layout.PlayerNameOffset + (i * Layout.PlayerNameSize);
                if (ReadName(offset) != old)
                    continue;
                var dest = Data.AsSpan(offset, Layout.PlayerNameSize);
                dest.Clear();
                bytes.CopyTo(dest);
            }
        }
    }

    public bool HasLifeCoins => Layout.LifeCoinsOffset != 0;

    public uint LifeCoins
    {
        get
        {
            if (!HasLifeCoins)
                throw new InvalidOperationException("Life Coins are not supported for this save version.");
            return ReadUInt32LittleEndian(Data.AsSpan(Layout.LifeCoinsOffset));
        }
        set
        {
            var current = LifeCoins;
            if (value == current)
                return;
            if (value > MaxLifeCoins)
                throw new ArgumentException($"Life Coins must be between 0 and {MaxLifeCoins:N0}.");
            WriteUInt32LittleEndian(Data.AsSpan(Layout.LifeCoinsOffset), value);
        }
    }

    private int GetNameOffset()
    {
        for (int i = 0; i < Layout.PlayerNameCount; i++)
        {
            var offset = Layout.PlayerNameOffset + (i * Layout.PlayerNameSize);
            if (Data[offset] != 0)
                return offset;
        }
        return Layout.PlayerNameOffset;
    }

    private string ReadName(int offset)
    {
        var data = Data.AsSpan(offset, Layout.PlayerNameSize);
        var end = data.IndexOf((byte)0);
        if (end < 0)
            throw new InvalidDataException("The player name is not terminated.");
        return Encoding.GetString(data[..end]);
    }

    public static SaveFile Read(byte[] data)
    {
        if (data.Length < 0x130 || ReadUInt32LittleEndian(data) != Magic)
            throw new InvalidDataException("This is not a supported common save file.");
        var layout = SaveLayout.GetLayout(ReadUInt32LittleEndian(data.AsSpan(4)));
        if (ReadUInt32LittleEndian(data.AsSpan(8)) != 0x40001)
            throw new InvalidDataException("This save version is not supported.");
        var length = ReadInt32LittleEndian(data.AsSpan(0x10));
        if (length <= layout.HeaderSize || length > data.Length)
            throw new InvalidDataException("The compressed save length is invalid.");

        using var input = new MemoryStream(data, layout.HeaderSize, length - layout.HeaderSize, false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var raw = new byte[layout.DataSize];
        zlib.ReadExactly(raw);
        if (zlib.ReadByte() != -1)
            throw new InvalidDataException("The common save layout is not supported.");
        var result = new SaveFile((byte[])data.Clone(), raw, layout);
        _ = result.PlayerName;
        return result;
    }

    public byte[] Write()
    {
        if (!IsEdited)
            return (byte[])Original.Clone();

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            zlib.Write(Data);
        var length = checked(Layout.HeaderSize + (int)compressed.Length);
        // Keep the original file capacity when compression shrinks. The header already records the active length
        var result = new byte[Math.Max(length, Original.Length)];
        Original.AsSpan(0, Layout.HeaderSize).CopyTo(result);
        compressed.GetBuffer().AsSpan(0, (int)compressed.Length).CopyTo(result.AsSpan(Layout.HeaderSize));
        WriteInt32LittleEndian(result.AsSpan(0x10), length);
        return result;
    }
}
