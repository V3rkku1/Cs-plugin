using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// Server-side visuals for hacker mode. A real client overlay is not possible from the server, so boxes
/// and the skeleton are beams in the world, and the name plate is world text. CheckTransmit sends each
/// one only to hackers who turned that visual on.
/// </summary>
public partial class LanSkinTester
{
    private const int BoxEdges = 12;
    private const int BoneEdges = 16;

    private sealed class PlayerEsp
    {
        public CEnvBeam?[] Box = new CEnvBeam?[BoxEdges];
        public CEnvBeam?[] Bones = new CEnvBeam?[BoneEdges];
        public CPointWorldText? Tag;
        public string TagShown = "";
        public Vector Anchor = new();
        public bool Anchored;
    }

    private readonly Dictionary<int, PlayerEsp> _esp = new();
    private bool _beamsBroken;

    private readonly record struct Gear(string Armor, string Gun, string Utility);

    private static Gear ReadGear(CCSPlayerPawn pawn)
    {
        var gun = "";
        var pistol = "";
        var util = new List<string>();
        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons != null)
        {
            foreach (var handle in weapons)
            {
                var name = handle.Value?.DesignerName;
                if (string.IsNullOrEmpty(name) || ItemData.IsKnifeClassname(name)) continue;
                var shortName = ShortItem(name);
                if (IsUtilityName(name)) util.Add(shortName);
                else if (IsPistol(name)) pistol = shortName;
                else if (gun == "") gun = shortName;
            }
        }
        if (gun == "") gun = pistol;

        var armor = pawn.ArmorValue;
        var helmet = false;
        var kit = false;
        try
        {
            if (pawn.ItemServices != null)
            {
                var items = new CCSPlayer_ItemServices(pawn.ItemServices.Handle);
                helmet = items.HasHelmet;
                kit = items.HasDefuser;
            }
        }
        catch (Exception)
        {
            // Item services moved after a CS2 update; HP and the gun still show.
        }
        if (kit) util.Add("KIT");
        var armorText = armor <= 0 ? "no armor" : helmet ? $"{armor} helm" : $"{armor} arm";
        return new Gear(armorText, gun, string.Join(" ", util));
    }

    private static bool IsPistol(string designer) => designer is "weapon_deagle" or "weapon_elite" or "weapon_fiveseven"
        or "weapon_glock" or "weapon_hkp2000" or "weapon_p250" or "weapon_usp_silencer" or "weapon_tec9"
        or "weapon_cz75a" or "weapon_revolver" or "weapon_taser";

    private static bool IsUtilityName(string designer) =>
        designer.Contains("grenade", StringComparison.Ordinal) || designer.Contains("molotov", StringComparison.Ordinal)
        || designer.Contains("flash", StringComparison.Ordinal) || designer.Contains("c4", StringComparison.Ordinal)
        || designer.Contains("decoy", StringComparison.Ordinal) || designer.Contains("breach", StringComparison.Ordinal);

    private static string ShortItem(string designer)
    {
        var n = designer.StartsWith("weapon_", StringComparison.Ordinal) ? designer["weapon_".Length..] : designer;
        return n switch
        {
            "ak47" => "AK",
            "m4a1" => "M4A4",
            "m4a1_silencer" => "M4A1",
            "hegrenade" => "HE",
            "flashbang" => "FL",
            "smokegrenade" => "SM",
            "molotov" => "MO",
            "incgrenade" => "IN",
            "decoy" => "DE",
            "c4" => "BOMB",
            "hkp2000" => "P2000",
            "usp_silencer" => "USP",
            "fiveseven" => "57",
            "elite" => "DUAL",
            "awp" => "AWP",
            "ssg08" => "SCOUT",
            "galilar" => "GALIL",
            "sg556" => "SG",
            "aug" => "AUG",
            "famas" => "FAMAS",
            "mac10" => "MAC10",
            "mp9" => "MP9",
            "mp7" => "MP7",
            "mp5sd" => "MP5",
            "ump45" => "UMP",
            "p90" => "P90",
            "bizon" => "BIZON",
            "nova" => "NOVA",
            "xm1014" => "XM",
            "mag7" => "MAG7",
            "sawedoff" => "SAWED",
            "m249" => "M249",
            "negev" => "NEGEV",
            "glock" => "GLOCK",
            "deagle" => "DEAGLE",
            "tec9" => "TEC9",
            "cz75a" => "CZ",
            "p250" => "P250",
            "revolver" => "R8",
            "taser" => "ZEUS",
            _ => n.ToUpperInvariant(),
        };
    }

    private bool VisualsWanted => Hackers().Any(h => Has(h, Power.BoxEsp) || Has(h, Power.BoneEsp) || Has(h, Power.Tags));

    private void UpdatePlayerEsp()
    {
        if (!VisualsWanted)
        {
            ClearPlayerEsp();
            return;
        }

        var alive = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || !player.PawnIsAlive || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;
            if (player.PlayerPawn.Value is not { IsValid: true } pawn || pawn.AbsOrigin == null) continue;
            alive.Add(player.Slot);
            try
            {
                DrawPlayerEsp(player, pawn, _esp.TryGetValue(player.Slot, out var existing) ? existing : _esp[player.Slot] = new PlayerEsp());
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "ESP update for slot {Slot}", player.Slot);
            }
        }
        foreach (var slot in _esp.Keys.ToList())
            if (!alive.Contains(slot)) RemovePlayerEsp(slot);
    }

    private void DrawPlayerEsp(CCSPlayerController player, CCSPlayerPawn pawn, PlayerEsp esp)
    {
        var origin = pawn.AbsOrigin!;
        var color = player.Team == CsTeam.Terrorist ? Color.FromArgb(255, 255, 80, 50) : Color.FromArgb(255, 90, 170, 255);
        var wantBox = Hackers().Any(h => Has(h, Power.BoxEsp));
        var wantBones = Hackers().Any(h => Has(h, Power.BoneEsp));
        var wantTags = Hackers().Any(h => Has(h, Power.Tags));

        // A beam that is dragged behind a running player keeps its old end on the client and draws a
        // streak. Rebuild it once the body has actually moved, instead of stretching the old one.
        var moved = !esp.Anchored || DistanceSquared(esp.Anchor, origin) > 4f * 4f;
        if (moved)
        {
            HideOwnedBeams(esp.Box);
            HideOwnedBeams(esp.Bones);
            esp.Anchor = new Vector(origin.X, origin.Y, origin.Z);
            esp.Anchored = true;
        }

        if (wantBox) DrawBox(esp, origin, EyeHeight(pawn) > 55f ? 72f : 54f, color);
        else HideOwnedBeams(esp.Box);

        if (wantBones) DrawSkeleton(esp, pawn, origin, color);
        else HideOwnedBeams(esp.Bones);

        if (wantTags) DrawTag(esp, player, pawn, origin);
        else if (esp.Tag is { IsValid: true })
        {
            esp.Tag.Remove();
            esp.Tag = null;
            esp.TagShown = "";
        }
    }

    private void DrawBox(PlayerEsp esp, Vector origin, float height, Color color)
    {
        const float radius = 16f;
        var corners = new Vector[8];
        for (var i = 0; i < 8; i++)
        {
            corners[i] = new Vector(
                origin.X + ((i & 1) == 0 ? -radius : radius),
                origin.Y + ((i & 2) == 0 ? -radius : radius),
                origin.Z + ((i & 4) == 0 ? 0f : height));
        }
        (int A, int B)[] edges = [(0, 1), (1, 3), (3, 2), (2, 0), (4, 5), (5, 7), (7, 6), (6, 4), (0, 4), (1, 5), (2, 6), (3, 7)];
        for (var i = 0; i < edges.Length; i++)
        {
            var beam = EnsureBeam(esp.Box, i, color);
            if (beam == null) return;
            MoveBeam(beam, corners[edges[i].A], corners[edges[i].B], color);
        }
    }

    private void DrawSkeleton(PlayerEsp esp, CCSPlayerPawn pawn, Vector origin, Color color)
    {
        TryReadView(pawn, out _, out var yawDeg);
        var yaw = yawDeg * Math.PI / 180.0;
        var forward = new Vector((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0);
        var right = new Vector((float)Math.Sin(yaw), (float)-Math.Cos(yaw), 0);
        var duck = EyeHeight(pawn) <= 55f;
        var headZ = duck ? 46f : 68f;
        var neckZ = headZ - 8f;
        var chestZ = headZ - 18f;
        var pelvisZ = duck ? 22f : 36f;

        // A fixed pose. A swinging walk cycle, moved every tick, is what turned the limbs into streaks.
        const float reach = 5f;

        Vector At(float f, float r, float z) => new(
            origin.X + forward.X * f + right.X * r,
            origin.Y + forward.Y * f + right.Y * r,
            origin.Z + z);

        var head = At(0, 0, headZ);
        var neck = At(0, 0, neckZ);
        var chest = At(0, 0, chestZ);
        var pelvis = At(0, 0, pelvisZ);
        var shoulderL = At(0, 8, chestZ + 2);
        var shoulderR = At(0, -8, chestZ + 2);
        var elbowL = At(reach * 0.45f, 11, chestZ - 4);
        var elbowR = At(reach * 0.55f, -6, chestZ - 2);
        var handL = At(reach, 13, chestZ - 12);
        var handR = At(reach + 4, -2, chestZ - 8);
        var hipL = At(0, 5, pelvisZ);
        var hipR = At(0, -5, pelvisZ);
        var kneeL = At(2f, 5, pelvisZ * 0.45f);
        var kneeR = At(-2f, -5, pelvisZ * 0.45f);
        var footL = At(1f, 6, 2);
        var footR = At(-1f, -6, 2);

        (Vector A, Vector B)[] bones =
        [
            (head, neck), (neck, chest), (chest, pelvis),
            (chest, shoulderL), (shoulderL, elbowL), (elbowL, handL),
            (chest, shoulderR), (shoulderR, elbowR), (elbowR, handR),
            (pelvis, hipL), (hipL, kneeL), (kneeL, footL),
            (pelvis, hipR), (hipR, kneeR), (kneeR, footR),
            (head, At(0, 5, headZ)),
        ];
        for (var i = 0; i < bones.Length; i++)
        {
            var beam = EnsureBeam(esp.Bones, i, color);
            if (beam == null) return;
            MoveBeam(beam, bones[i].A, bones[i].B, color);
        }
    }

    private void DrawTag(PlayerEsp esp, CCSPlayerController player, CCSPlayerPawn pawn, Vector origin)
    {
        if (_tick % 8 == 0 || esp.TagShown == "")
        {
            var gear = ReadGear(pawn);
            var name = player.PlayerName ?? "";
            if (name.Length > 24) name = name[..24];
            var line2 = $"{pawn.Health} HP  {gear.Armor}";
            var line3 = gear.Gun == "" ? gear.Utility : gear.Utility == "" ? gear.Gun : $"{gear.Gun}  {gear.Utility}";
            var text = line3 == "" ? $"{name}\n{line2}" : $"{name}\n{line2}\n{line3}";
            if (esp.Tag == null || !esp.Tag.IsValid)
            {
                esp.Tag = CreateEspTag(text, player.Team == CsTeam.Terrorist
                    ? Color.FromArgb(255, 255, 150, 110)
                    : Color.FromArgb(255, 170, 210, 255));
                esp.TagShown = text;
            }
            else if (text != esp.TagShown)
            {
                esp.Tag.AcceptInput("SetMessage", esp.Tag, esp.Tag, text);
                esp.TagShown = text;
            }
        }
        if (esp.Tag is { IsValid: true })
        {
            TryReadView(pawn, out _, out var yaw);
            var z = origin.Z + Math.Max(EyeHeight(pawn), 64f) + 18f;
            esp.Tag.Teleport(new Vector(origin.X, origin.Y, z), new QAngle(0, yaw, 0), null);
        }
    }

    private static CPointWorldText? CreateEspTag(string text, Color color)
    {
        var entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (entity == null) return null;
        entity.MessageText = text;
        entity.Enabled = true;
        entity.FontSize = 22f;
        entity.FontName = "Arial Bold";
        entity.Fullbright = true;
        entity.WorldUnitsPerPx = 0.28f;
        entity.Color = color;
        entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
        entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
        entity.DispatchSpawn();
        // Same through-wall highlight as the player chams, so the plate is readable behind cover.
        entity.Glow.GlowColorOverride = color;
        entity.Glow.GlowRange = 8000;
        entity.Glow.GlowRangeMin = 0;
        entity.Glow.GlowTeam = -1;
        entity.Glow.GlowType = 3;
        return entity;
    }

    private CEnvBeam? EnsureBeam(CEnvBeam?[] slots, int index, Color color)
    {
        var beam = slots[index];
        if (beam is { IsValid: true }) return beam;
        beam = CreateBeam(color);
        slots[index] = beam;
        return beam;
    }

    private CEnvBeam? CreateBeam(Color color)
    {
        if (_beamsBroken) return null;
        try
        {
            var beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam");
            if (beam == null)
            {
                _beamsBroken = true;
                Logger.LogWarning("Box and skeleton ESP need env_beam, and this server did not create one");
                return null;
            }
            beam.BeamType = BeamType_t.BEAM_POINTS;
            beam.Width = 2f;
            beam.EndWidth = 2f;
            beam.BoltWidth = 2f;
            beam.Amplitude = 0;
            beam.NoiseAmplitude = 0;
            beam.Life = 9999f;
            beam.SpriteName = "materials/sprites/laserbeam.vmat";
            beam.BeamFlags = 0x4000u | 0x20u; // stay up, draw solid
            beam.NumBeamEnts = 2;
            beam.Render = color;
            beam.RenderMode = RenderMode_t.kRenderNormal;
            beam.NoInterpolate = true;
            beam.Speed = 0;
            beam.FadeMinDist = 0;
            beam.FadeMaxDist = 20000;
            beam.DispatchSpawn();
            beam.Glow.GlowColorOverride = color;
            beam.Glow.GlowRange = 8000;
            beam.Glow.GlowRangeMin = 0;
            beam.Glow.GlowTeam = -1;
            beam.Glow.GlowType = 3;
            return beam;
        }
        catch (Exception ex)
        {
            _beamsBroken = true;
            Logger.LogWarning(ex, "Could not create an ESP beam");
            return null;
        }
    }

    private static void MoveBeam(CEnvBeam beam, Vector start, Vector end, Color color)
    {
        if (!beam.IsValid) return;
        beam.Render = color;
        beam.NoInterpolate = true;
        beam.Teleport(start, new QAngle(), new Vector());
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        beam.EndPointWorld.X = end.X;
        beam.EndPointWorld.Y = end.Y;
        beam.EndPointWorld.Z = end.Z;
        beam.EndPointRelative.X = 0;
        beam.EndPointRelative.Y = 0;
        beam.EndPointRelative.Z = 0;
        try
        {
            Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
            Utilities.SetStateChanged(beam, "CEnvBeam", "m_vecEndPointWorld");
        }
        catch (Exception)
        {
            // The network name moved. Rebuilding the beam after a move still drops the streak.
        }
    }

    private static float DistanceSquared(Vector a, Vector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private static void HideOwnedBeams(CEnvBeam?[] slots)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { IsValid: true } beam) beam.Remove();
            slots[i] = null;
        }
    }

    private void RemovePlayerEsp(int slot)
    {
        if (!_esp.Remove(slot, out var esp)) return;
        DestroyEsp(esp);
    }

    private void ClearPlayerEsp()
    {
        if (_esp.Count == 0) return;
        foreach (var esp in _esp.Values) DestroyEsp(esp);
        _esp.Clear();
    }

    private static void DestroyEsp(PlayerEsp esp)
    {
        HideOwnedBeams(esp.Box);
        HideOwnedBeams(esp.Bones);
        if (esp.Tag is { IsValid: true }) esp.Tag.Remove();
        esp.Tag = null;
    }

    private bool CanSeeVisual(CCSPlayerController viewer, CCSPlayerController owner, Power power) =>
        viewer.Slot != owner.Slot && viewer.IsValid && owner.IsValid && IsHacker(viewer) && Has(viewer, power);

    private void FilterEsp(CCheckTransmitInfo info, CCSPlayerController? viewer)
    {
        foreach (var (slot, esp) in _esp)
        {
            var owner = Utilities.GetPlayerFromSlot(slot);
            var box = owner != null && viewer != null && CanSeeVisual(viewer, owner, Power.BoxEsp);
            var bones = owner != null && viewer != null && CanSeeVisual(viewer, owner, Power.BoneEsp);
            var tags = owner != null && viewer != null && CanSeeVisual(viewer, owner, Power.Tags);
            if (!box) DropBeams(info, esp.Box);
            if (!bones) DropBeams(info, esp.Bones);
            if (!tags && esp.Tag is { IsValid: true }) info.TransmitEntities.Remove(esp.Tag);
        }
    }

    private static void DropBeams(CCheckTransmitInfo info, CEnvBeam?[] slots)
    {
        foreach (var beam in slots)
            if (beam is { IsValid: true }) info.TransmitEntities.Remove(beam);
    }
}
