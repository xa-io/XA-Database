using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace XADatabase.Collectors;

/// <summary>
/// Phase 3c probe: comprehensive scan of InfoProxyFreeCompany for FC points and estate data.
/// </summary>
public static class HousingProbe
{
    public static unsafe void Probe()
    {
        Plugin.Log.Information("[XA] [Probe] === Phase 3c: Full FC struct scan ===");

        try
        {
            var proxy = InfoProxyFreeCompany.Instance();
            if (proxy == null)
            {
                Plugin.Log.Information("[XA] [Probe] InfoProxyFreeCompany: null");
                return;
            }

            Plugin.Log.Information($"[XA] [Probe] FC Id={proxy->Id} Rank={proxy->Rank} Members={proxy->TotalMembers}");

            byte* basePtr = (byte*)proxy;

            // ── 1. Search entire struct for FC points value 40070 (0x9C86) ──
            // Scan as uint16 at every 2-byte boundary
            Plugin.Log.Information("[XA] [Probe] Searching for 40070 (0x9C86) as uint16:");
            for (int offset = 0; offset <= 0x6E6; offset += 2)
            {
                ushort val = *(ushort*)(basePtr + offset);
                if (val == 40070)
                    Plugin.Log.Information($"[XA] [Probe]   FOUND uint16 at 0x{offset:X3} = {val}");
            }

            // Scan as uint32 at every 4-byte boundary
            Plugin.Log.Information("[XA] [Probe] Searching for 40070 (0x9C86) as uint32:");
            for (int offset = 0; offset <= 0x6E4; offset += 4)
            {
                uint val = *(uint*)(basePtr + offset);
                if (val == 40070)
                    Plugin.Log.Information($"[XA] [Probe]   FOUND uint32 at 0x{offset:X3} = {val}");
            }

            // ── 2. Dump Utf8String at 0xD0 (may contain estate name) ──
            // Utf8String struct: first 8 bytes are vtable, then pointer to string data at +0x08...
            // Actually just read raw bytes at 0xD0..0x138 for any readable text
            Plugin.Log.Information("[XA] [Probe] Raw text scan 0xD0..0x138:");
            try
            {
                var sb = new StringBuilder();
                for (int offset = 0xD0; offset < 0x138; offset++)
                {
                    byte b = basePtr[offset];
                    if (b >= 0x20 && b < 0x7F)
                        sb.Append((char)b);
                    else if (sb.Length > 2)
                    {
                        Plugin.Log.Information($"[XA] [Probe]   Text at ~0x{offset - sb.Length:X3}: \"{sb}\"");
                        sb.Clear();
                    }
                    else
                        sb.Clear();
                }
                if (sb.Length > 2)
                    Plugin.Log.Information($"[XA] [Probe]   Text at ~0x{0x138 - sb.Length:X3}: \"{sb}\"");
            }
            catch (Exception ex)
            {
                Plugin.Log.Information($"[XA] [Probe] Text scan error: {ex.Message}");
            }

            // ── 3. Dump interesting uint16 values in 0xC0..0x178 (between known fields) ──
            // Looking for ward (9 or 8), plot (4 or 3), district ID, and FC points
            Plugin.Log.Information("[XA] [Probe] uint16 scan 0xC0..0x178 (non-zero, <65535):");
            for (int offset = 0xC0; offset <= 0x176; offset += 2)
            {
                ushort val = *(ushort*)(basePtr + offset);
                if (val != 0 && val < 60000)
                    Plugin.Log.Information($"[XA] [Probe]   0x{offset:X3} u16={val}");
            }

            // ── 4. Also scan 0x658..0x6E8 for uint16 values ──
            Plugin.Log.Information("[XA] [Probe] uint16 scan 0x658..0x6E8:");
            for (int offset = 0x658; offset <= 0x6E6; offset += 2)
            {
                ushort val = *(ushort*)(basePtr + offset);
                if (val != 0 && val < 60000)
                    Plugin.Log.Information($"[XA] [Probe]   0x{offset:X3} u16={val}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Information($"[XA] [Probe] FC probe error: {ex.Message}");
        }

        Plugin.Log.Information("[XA] [Probe] === Phase 3c complete ===");
    }
}
