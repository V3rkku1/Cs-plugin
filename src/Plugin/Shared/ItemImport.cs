using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LanSkinTester;

/// <summary>An item read from an inspect link or a !gen code.</summary>
public class ImportedItem
{
    public ushort DefIndex { get; set; }
    public SkinSettings Skin { get; set; } = new();
}

/// <summary>
/// Reads real items from:
/// <list type="bullet">
/// <item>modern inspect links (<c>csgo_econ_action_preview 00180720...</c>): the item data is inside the link</item>
/// <item>gen codes (<c>!gen 7 282 661 0.15 [sticker wear]x5 [charm ...]</c>), e.g. "Copy !gen" on CSFloat</item>
/// </list>
/// Old Steam Market links (<c>...M123A456D789</c>) only point to an item on Valve's servers and need a logged-in
/// Steam bot to resolve, so they are rejected with an explanation.
/// </summary>
public static class ItemImport
{
    private static readonly Regex HexPayload = new(@"csgo_econ_action_preview(?:\s+|%20)?([0-9A-Fa-f]{10,})\s*$");
    private static readonly Regex MarketPointer = new(@"csgo_econ_action_preview(?:\s+|%20)?[SM]\d+A\d+D\d+", RegexOptions.IgnoreCase);
    private static readonly Regex BareHex = new(@"^[0-9A-Fa-f]{10,}$");

    public const string OldMarketLinkMessage =
        "That is an old-style Steam Market link (…M…A…D…): it only points to the item on Valve's servers and has " +
        "no skin data inside. Open the listing on csfloat.com and use \"Copy !gen\" (or an inspect link that " +
        "starts with csgo_econ_action_preview 00…), then paste that here.";

    public static bool TryParse(string text, out ImportedItem item, out string error)
    {
        item = new ImportedItem();
        error = "";
        text = Uri.UnescapeDataString(text.Trim()).Trim();

        if (MarketPointer.IsMatch(text))
        {
            error = OldMarketLinkMessage;
            return false;
        }

        var hex = HexPayload.Match(text);
        if (hex.Success || BareHex.IsMatch(text))
            return TryParseHex(hex.Success ? hex.Groups[1].Value : text, out item, out error);

        if (text.StartsWith('!') || text.StartsWith("gen", StringComparison.OrdinalIgnoreCase) || char.IsDigit(text.FirstOrDefault()))
            return TryParseGen(text, out item, out error);

        error = "Paste an inspect link (csgo_econ_action_preview 00…) or a !gen code.";
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Gen codes
    // ---------------------------------------------------------------------------------------------

    /// <summary>!gen defindex paint seed float [sticker_id sticker_wear] x5 [charm_id charm_wear ...]</summary>
    public static bool TryParseGen(string text, out ImportedItem item, out string error)
    {
        item = new ImportedItem();
        error = "";

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && !char.IsDigit(parts[0][0])) parts.RemoveAt(0); // !gen, !g, !gengl, gen
        if (parts.Count < 4)
        {
            error = "A gen code needs at least: defindex paint seed float (e.g. !gen 7 282 661 0.15).";
            return false;
        }

        if (!ushort.TryParse(parts[0], out var def) || !int.TryParse(parts[1], out var paint) ||
            !int.TryParse(parts[2], out var seed) || !TryFloat(parts[3], out var wear))
        {
            error = "Could not read the gen code numbers (defindex paint seed float).";
            return false;
        }

        item.DefIndex = def;
        item.Skin = new SkinSettings { Paint = paint, Seed = seed, Wear = ClampWear(wear) };

        // Then up to 5 sticker pairs (id wear), "0 0" = empty slot, then optional charm pairs.
        var rest = parts.Skip(4).ToList();
        for (int i = 0, slot = 0; i + 1 < rest.Count; i += 2, slot++)
        {
            if (!int.TryParse(rest[i], out var id) || !TryFloat(rest[i + 1], out var stickerWear)) break;
            if (slot < 5)
            {
                if (id != 0) item.Skin.Stickers.Add(new StickerSettings { Slot = slot, Id = id, Wear = Math.Clamp(stickerWear, 0f, 1f) });
            }
            else if (id != 0 && item.Skin.Keychain == null)
            {
                item.Skin.Keychain = new KeychainSettings { Id = id };
            }
        }
        return true;
    }

    public static string ToGen(ushort defIndex, SkinSettings skin)
    {
        var sb = new StringBuilder($"!gen {defIndex} {skin.Paint} {skin.Seed} {F(skin.Wear)}");
        for (var slot = 0; slot < 5; slot++)
        {
            var st = skin.Stickers.FirstOrDefault(s => s.Slot == slot);
            sb.Append(st == null ? " 0 0" : $" {st.Id} {F(st.Wear)}");
        }
        if (skin.Keychain != null) sb.Append($" {skin.Keychain.Id} 0");
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Inspect links: hex( key, protobuf CEconItemPreviewDataBlock, 4-byte checksum ), XOR'd with key
    // ---------------------------------------------------------------------------------------------

    public static bool TryParseHex(string hexText, out ImportedItem item, out string error)
    {
        item = new ImportedItem();
        error = "";

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hexText);
        }
        catch (FormatException)
        {
            error = "The inspect link data is not valid hex.";
            return false;
        }

        if (bytes.Length < 6)
        {
            error = "The inspect link is too short.";
            return false;
        }

        // Game-made links XOR every byte with the first one; after unmasking the first byte is 0.
        var key = bytes[0];
        if (key != 0)
            for (var i = 0; i < bytes.Length; i++) bytes[i] ^= key;

        try
        {
            item = Decode(bytes.AsSpan(1, bytes.Length - 5));
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            error = "Could not read the item inside the inspect link.";
            return false;
        }

        if (item.DefIndex == 0)
        {
            error = "The inspect link has no item in it.";
            return false;
        }
        return true;
    }

    /// <summary>Creates a modern inspect link for sharing (same format the game and CSFloat use).</summary>
    public static string ToInspectLink(ushort defIndex, SkinSettings skin)
    {
        var w = new ProtoWriter();
        w.Varint(3, defIndex);
        w.Varint(4, (ulong)skin.Paint);
        w.Varint(6, (ulong)(skin.StatTrak ? 9 : ItemData.IsKnife(defIndex) || defIndex >= 4725 ? 3 : 4));
        w.Varint(7, BitConverter.SingleToUInt32Bits(skin.Wear));
        w.Varint(8, (ulong)skin.Seed);
        if (skin.StatTrak)
        {
            w.Varint(9, 0);
            w.Varint(10, (ulong)skin.StatTrakCount);
        }
        if (skin.Nametag != "") w.Bytes(11, Encoding.UTF8.GetBytes(skin.Nametag));
        foreach (var st in skin.Stickers)
        {
            var s = new ProtoWriter();
            s.Varint(1, (ulong)st.Slot);
            s.Varint(2, (ulong)st.Id);
            if (st.Wear != 0) s.Fixed32(3, st.Wear);
            w.Bytes(12, s.ToArray());
        }
        if (skin.Keychain != null)
        {
            var k = new ProtoWriter();
            k.Varint(1, 0);
            k.Varint(2, (ulong)skin.Keychain.Id);
            if (skin.Keychain.Seed != 0) k.Varint(10, (ulong)skin.Keychain.Seed);
            w.Bytes(20, k.ToArray());
        }

        var proto = w.ToArray();
        var buffer = new byte[proto.Length + 5];
        proto.CopyTo(buffer, 1);
        var crc = Crc32(buffer.AsSpan(0, proto.Length + 1));
        var check = (crc & 0xFFFF) ^ (uint)(proto.Length * crc);
        buffer[^4] = (byte)(check >> 24);
        buffer[^3] = (byte)(check >> 16);
        buffer[^2] = (byte)(check >> 8);
        buffer[^1] = (byte)check;
        return "steam://run/730//+csgo_econ_action_preview%20" + Convert.ToHexString(buffer);
    }

    private static ImportedItem Decode(ReadOnlySpan<byte> data)
    {
        var item = new ImportedItem();
        var skin = item.Skin;
        int quality = 0;
        var r = new ProtoReader(data);

        while (r.Next(out var field, out var type))
        {
            switch (field)
            {
                case 3: item.DefIndex = (ushort)r.ReadVarint(); break;
                case 4: skin.Paint = (int)r.ReadVarint(); break;
                case 6: quality = (int)r.ReadVarint(); break;
                case 7: skin.Wear = ClampWear(BitConverter.UInt32BitsToSingle((uint)r.ReadVarint())); break;
                case 8: skin.Seed = (int)r.ReadVarint(); break;
                case 10:
                    skin.StatTrak = true;
                    skin.StatTrakCount = (int)r.ReadVarint();
                    break;
                case 11: skin.Nametag = Encoding.UTF8.GetString(r.ReadBytes()); break;
                case 12:
                    var sticker = ReadSticker(r.ReadBytes());
                    if (sticker.Id != 0 && sticker.Slot < 5) skin.Stickers.Add(sticker);
                    break;
                case 20:
                    var charm = ReadSticker(r.ReadBytes(), out var pattern);
                    if (charm.Id != 0 && skin.Keychain == null)
                        skin.Keychain = new KeychainSettings { Id = charm.Id, OffsetX = charm.OffsetX, OffsetY = charm.OffsetY, Seed = pattern };
                    break;
                default: r.Skip(type); break;
            }
        }

        if (quality == 9) skin.StatTrak = true; // StatTrak quality even if the counter field is missing
        return item;
    }

    private static StickerSettings ReadSticker(ReadOnlySpan<byte> data) => ReadSticker(data, out _);

    private static StickerSettings ReadSticker(ReadOnlySpan<byte> data, out int pattern)
    {
        var s = new StickerSettings();
        pattern = 0;
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var type))
        {
            switch (field)
            {
                case 1: s.Slot = (int)r.ReadVarint(); break;
                case 2: s.Id = (int)r.ReadVarint(); break;
                case 3: s.Wear = r.ReadFloat(); break;
                case 4: s.Scale = r.ReadFloat(); break;
                case 5: s.Rotation = r.ReadFloat(); break;
                case 7: s.OffsetX = r.ReadFloat(); break;
                case 8: s.OffsetY = r.ReadFloat(); break;
                case 10: pattern = (int)r.ReadVarint(); break;
                default: r.Skip(type); break;
            }
        }
        if (s.Scale == 0) s.Scale = 1;
        return s;
    }

    // Standard CRC-32 (zlib), used by the inspect link checksum.
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private static float ClampWear(float wear) => float.IsFinite(wear) ? Math.Clamp(wear, 0.00001f, 1f) : 0.01f;

    private static bool TryFloat(string s, out float value) =>
        float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string F(float f) => f.ToString("0.########", CultureInfo.InvariantCulture);

    // Minimal protobuf reader/writer: only what CEconItemPreviewDataBlock needs.
    private ref struct ProtoReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;

        public ProtoReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _pos = 0;
        }

        public bool Next(out int field, out int type)
        {
            field = type = 0;
            if (_pos >= _data.Length) return false;
            var tag = ReadVarint();
            field = (int)(tag >> 3);
            type = (int)(tag & 7);
            if (field == 0) throw new InvalidDataException("field 0");
            return true;
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var b = _data[_pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
            }
            throw new InvalidDataException("varint too long");
        }

        public float ReadFloat()
        {
            var v = BitConverter.ToSingle(_data.Slice(_pos, 4));
            _pos += 4;
            return v;
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            var len = (int)ReadVarint();
            var slice = _data.Slice(_pos, len);
            _pos += len;
            return slice;
        }

        public void Skip(int type)
        {
            switch (type)
            {
                case 0: ReadVarint(); break;
                case 1: _pos += 8; break;
                case 2: ReadBytes(); break;
                case 5: _pos += 4; break;
                default: throw new InvalidDataException($"wire type {type}");
            }
            if (_pos > _data.Length) throw new InvalidDataException("truncated");
        }
    }

    private sealed class ProtoWriter
    {
        private readonly MemoryStream _out = new();

        public void Varint(int field, ulong value)
        {
            WriteRaw((ulong)field << 3);
            WriteRaw(value);
        }

        public void Fixed32(int field, float value)
        {
            WriteRaw(((ulong)field << 3) | 5);
            _out.Write(BitConverter.GetBytes(value));
        }

        public void Bytes(int field, byte[] value)
        {
            WriteRaw(((ulong)field << 3) | 2);
            WriteRaw((ulong)value.Length);
            _out.Write(value);
        }

        public byte[] ToArray() => _out.ToArray();

        private void WriteRaw(ulong v)
        {
            while (v >= 0x80)
            {
                _out.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            _out.WriteByte((byte)v);
        }
    }
}
