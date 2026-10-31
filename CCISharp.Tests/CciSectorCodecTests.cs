namespace CCISharp.Tests;

public sealed class CciSectorCodecTests
{
    [Fact]
    public void ZeroSector_CompressesAndRoundTrips()
    {
        byte[] sector = new byte[CciFormat.SectorSize];
        byte[] encoded = new byte[CciFormat.SectorSize];
        byte[] decoded = new byte[CciFormat.SectorSize];
        int[] table = new int[Lz4.HashTableSize];

        Assert.Equal(
            CciError.None,
            CciSectorCodec.TryEncode(sector, encoded, 1, table, out int written, out bool compressed));

        Assert.True(compressed);
        Assert.True(written < CciFormat.SectorSize);
        Assert.Equal(CciError.None, CciSectorCodec.TryDecode(encoded.AsSpan(0, written), compressed, decoded));
        Assert.Equal(sector, decoded);
    }

    [Fact]
    public void ConstantSector_CompressesAndRoundTrips()
    {
        byte[] sector = new byte[CciFormat.SectorSize];
        sector.AsSpan().Fill(0x5A);
        byte[] encoded = new byte[CciFormat.SectorSize];
        byte[] decoded = new byte[CciFormat.SectorSize];
        int[] table = new int[Lz4.HashTableSize];

        Assert.Equal(
            CciError.None,
            CciSectorCodec.TryEncode(sector, encoded, 1, table, out int written, out bool compressed));

        Assert.True(compressed);
        Assert.Equal(CciError.None, CciSectorCodec.TryDecode(encoded.AsSpan(0, written), compressed, decoded));
        Assert.Equal(sector, decoded);
    }

    [Fact]
    public void RandomSector_StoresRawAndRoundTrips()
    {
        byte[] sector = TestImages.BuildRandom(1);
        byte[] encoded = new byte[CciFormat.SectorSize];
        byte[] decoded = new byte[CciFormat.SectorSize];
        int[] table = new int[Lz4.HashTableSize];

        Assert.Equal(
            CciError.None,
            CciSectorCodec.TryEncode(sector, encoded, 1, table, out int written, out bool compressed));

        Assert.False(compressed);
        Assert.Equal(CciFormat.SectorSize, written);
        Assert.Equal(CciError.None, CciSectorCodec.TryDecode(encoded, false, decoded));
        Assert.Equal(sector, decoded);
    }

    [Fact]
    public void LevelZero_StoresRaw()
    {
        byte[] sector = new byte[CciFormat.SectorSize];
        byte[] encoded = new byte[CciFormat.SectorSize];
        int[] table = new int[Lz4.HashTableSize];

        Assert.Equal(
            CciError.None,
            CciSectorCodec.TryEncode(sector, encoded, 0, table, out int written, out bool compressed));

        Assert.False(compressed);
        Assert.Equal(CciFormat.SectorSize, written);
        Assert.Equal(sector, encoded);
    }

    [Fact]
    public void Decode_BadPad_ReturnsCorrupt()
    {
        byte[] blob = new byte[16];
        blob[0] = 255;
        byte[] sector = new byte[CciFormat.SectorSize];

        Assert.Equal(CciError.CorruptSector, CciSectorCodec.TryDecode(blob, true, sector));
    }

    [Fact]
    public void Decode_RawWrongLength_ReturnsCorrupt()
    {
        byte[] blob = new byte[100];
        byte[] sector = new byte[CciFormat.SectorSize];

        Assert.Equal(CciError.CorruptSector, CciSectorCodec.TryDecode(blob, false, sector));
    }

    [Fact]
    public void Decode_TruncatedCompressedBlock_ReturnsCorrupt()
    {
        byte[] blob = [0, 1, 2];
        byte[] sector = new byte[CciFormat.SectorSize];

        Assert.Equal(CciError.CorruptSector, CciSectorCodec.TryDecode(blob, true, sector));
    }
}
