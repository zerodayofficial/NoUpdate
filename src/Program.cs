using System;
using System.Runtime.InteropServices;
using SharpProspero.Payload;
using SharpProspero.Payload.IO;
using SharpProspero.Payload.Kernel;
using SharpProspero.Payload.Posix;
using SharpProspero.Payload.Process;
using SharpProspero.Payload.Services;

namespace NoUpdate;

internal static unsafe class Program
{
    private const int MaxTextScanBytes = 64 * 1024 * 1024; 
    private const int MaxSitesA = 16;
    private const int MaxSitesB = 32;
    private const int MaxParamBytes = 64 * 1024;
    private const int MaxGames = 96;
    private const int MaxNameBytes = 128;
    private const int MaxVersionBytes = 32;

    private const uint MinFw = 0x06000000; 
    private const uint MaxFw = 0x13600000; 

    
    private const uint RegNetIpDnsFlag = 0x14140400;
    private const uint RegNetIpPrimaryDns = 0x14140500;
    private const uint RegNetIpSecondaryDns = 0x14140600;
    private const uint RegWifiIpDnsFlag = 0x141E6008;
    private const uint RegWifiIpPrimaryDns = 0x141E6009;
    private const uint RegWifiIpSecondaryDns = 0x141E600A;

    
    
    
    private const uint RegNetCurrentProfile = 0x14190B00;
    private const uint RegNetProfileDnsFlag = 0x143C4808;
    private const uint RegNetProfileStride = 0x10000;

    private const int FlagFw = 1;
    private const int FlagUpdate = 2;

    private const int PersistOk = 1;
    private const int PersistFail = 2;
    private const int MaxParamFileBytes = 16 * 1024;
    private const int MaxPersistPathBytes = 96;

    
    
    
    
    
    
    private const bool EnableContentVersionRewrite = true;

    
    
    
    
    [UnmanagedCallersOnly(EntryPoint = "__dso_handle")]
    public static void DsoHandle()
    {
    }

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        PayloadCrt.Klog("Welcome to NoUpdate\n\0"u8);
        PayloadCrt.Klog("MADE BY ZER0DAY\n\0"u8);
        PayloadNotification.SendKernelNotification("Welcome to NoUpdate"u8);

        var pargs = PayloadEntryPoint.Args;
        if (pargs == null)
        {
            PayloadCrt.Klog("sp:cpb:no-args\n\0"u8);
            return -1;
        }

        var io = new PayloadKernelIo(pargs);

        DisableAutostart();

        uint fw = PayloadKernel.GetFirmwareVersion(io);
        if (fw == 0)
        {
            PayloadCrt.Klog("sp:cpb:fw-fail\n\0"u8);
            return 1;
        }
        KlogHex("sp:cpb:fw "u8, fw);
        if (fw < MinFw || fw > MaxFw)
            PayloadCrt.Klog("sp:cpb:fw-outside-6.00-13.60\n\0"u8);
        else
            PayloadCrt.Klog("sp:cpb:fw-in-range\n\0"u8);
        
        
        
        KlogHex("sp:cpb:allproc "u8, KernelOffsets.Allproc(fw));
        KlogHex("sp:cpb:pmap "u8, (ulong)KernelOffsets.KernelPmapStore(fw));
        KlogHex("sp:cpb:dynlib "u8, (ulong)(uint)KernelOffsets.ProcDynlib(fw));

        
        

        
        
        ulong dmapBase = 0;
        long pmapOff = KernelOffsets.KernelPmapStore(fw);
        if (pmapOff != 0)
        {
            ulong pmapStore = pargs->KernelDataBase + (ulong)pmapOff;
            ulong dmapVirt = io.ReadU64(pmapStore + 32);
            ulong pmCr3 = io.ReadU64(pmapStore + 40);
            if (dmapVirt != 0 && pmCr3 != 0)
                dmapBase = dmapVirt - pmCr3;
        }
        if (dmapBase == 0)
            PayloadCrt.Klog("sp:cpb:no-dmap\n\0"u8);

        
        
        
        
        byte* shellName = stackalloc byte[13];
        ReadOnlySpan<byte> shellEncoded = new byte[]
        {
            0x09, 0x39, 0x3F, 0x09, 0x32, 0x3F, 0x36,
            0x36, 0x19, 0x35, 0x28, 0x3F
        };
        for (int i = 0; i < shellEncoded.Length; i++)
            shellName[i] = (byte)(shellEncoded[i] ^ 0x5A);
        shellName[12] = 0;

        int pid = PayloadSysctl.FindPidByName(shellName);

        ulong proc = 0;
        if (pid <= 0)
        {
            proc = PayloadKernel.FindProcessByName(io, shellName, 12);
            if (proc != 0)
                pid = (int)io.ReadU32(proc + (ulong)KernelOffsets.ProcPid);
        }
        if (pid <= 0)
        {
            PayloadCrt.Klog("sp:cpb:proc-fail\n\0"u8);
            RunPersistentWork(fw, false);
            return 4;
        }
        KlogHex("sp:cpb:pid "u8, (ulong)pid);

        ulong cr3 = 0;
        if (pmapOff != 0)
            cr3 = KernelPaging.GetProcessCr3(io, pid, (fw >> 16) & 0xFFFF);
        if (cr3 == 0)
            PayloadCrt.Klog("sp:cpb:no-cr3\n\0"u8);

        
        
        
        ulong textStart = 0;
        ulong textSize = 0;
        int dynlibOffset = KernelOffsets.ProcDynlib(fw);
        if (dynlibOffset >= 0 && proc == 0)
        {
            proc = PayloadKernel.FindProcessByName(io, shellName, 12);
        }
        if (dynlibOffset >= 0 && proc != 0)
        {
            ulong pDynlib = io.ReadU64(proc + (ulong)dynlibOffset);
            if (pDynlib != 0)
            {
                ulong dsoEboot = io.ReadU64(pDynlib);
                if (dsoEboot != 0)
                {
                    ulong segments = io.ReadU64(dsoEboot + 0x40);
                    if (segments != 0)
                    {
                        textStart = io.ReadU64(segments + 0x08);
                        textSize = io.ReadU64(segments + 0x10);
                    }
                }
            }
        }
        if (textStart == 0 || textSize == 0)
        {
            PayloadCrt.Klog("sp:cpb:text-probe\n\0"u8);
            if (!FindTextByProbe(pid, out textStart, out textSize))
            {
                PayloadCrt.Klog("sp:cpb:text-none\n\0"u8);
                RunPersistentWork(fw, false);
                return 10;
            }
        }
        if (textSize > MaxTextScanBytes)
            textSize = MaxTextScanBytes;

        ulong textEnd = textStart + textSize;
        KlogHex("sp:cpb:text "u8, textStart);
        KlogHex("sp:cpb:size "u8, textSize);

        ulong* sitesA = stackalloc ulong[MaxSitesA];
        ulong* sitesB = stackalloc ulong[MaxSitesB];
        int countA = 0;
        int countB = 0;

        byte* cur3 = stackalloc byte[3];
        byte* cur6 = stackalloc byte[6];
        byte* patchA = stackalloc byte[3] { 0x45, 0x31, 0xFF };
        byte* patchB = stackalloc byte[6] { 0x45, 0x31, 0xFF, 0x90, 0x90, 0x90 };

        
        byte[] patA =
        {
            0xE8, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0xE8, 0x00, 0x00, 0x00, 0x00,
            0x45, 0x31, 0xF6,
            0x84, 0xC0,
            0x45, 0x0F, 0x44, 0xF7,
            0x45, 0x85, 0xF6,
            0x0F, 0x89, 0x00, 0x00, 0x00, 0x00
        };
        byte[] maskA =
        {
            0xFF, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0xFF, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF, 0xFF,
            0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00
        };

        ulong cursor = textStart;
        while (cursor < textEnd && countA < MaxSitesA)
        {
            nint m = PayloadProcessMemory.PatternScan(
                pid, (nint)cursor, (nuint)(textEnd - cursor), patA, maskA);
            if (m == 0)
                break;

            ulong site = (ulong)m + 5;
            if (PayloadProcessMemory.Read(pid, (nint)site, cur3, 3) == 0)
            {
                if (cur3[0] == 0x41 && cur3[1] == 0x89 && cur3[2] == 0xC7)
                {
                    if (WriteSite(pid, site, patchA, 3, io, cr3, dmapBase) &&
                        PayloadProcessMemory.Read(pid, (nint)site, cur3, 3) == 0 &&
                        cur3[0] == 0x45 && cur3[1] == 0x31 && cur3[2] == 0xFF)
                    {
                        sitesA[countA++] = site;
                        KlogHex("sp:cpb:a-patched "u8, site);
                    }
                    else
                    {
                        PayloadCrt.Klog("sp:cpb:a-write-fail\n\0"u8);
                    }
                }
                else if (cur3[0] == 0x45 && cur3[1] == 0x31 && cur3[2] == 0xFF)
                {
                    sitesA[countA++] = site;
                    KlogHex("sp:cpb:a-already "u8, site);
                }
            }

            cursor = (ulong)m + (ulong)patA.Length;
        }

        
        
        
        
        
        
        for (int i = 0; i < countA; i++)
        {
            ulong from = sitesA[i] > 0x10000 ? sitesA[i] - 0x10000 : textStart;
            ulong to = sitesA[i] + 0x10000;
            if (to > textEnd)
                to = textEnd;
            countB = ScanSiteB(pid, from, to, sitesB, countB, patchB, cur6, io, cr3, dmapBase);
            if (countB == 0)
                countB = ScanPatchedB(pid, from, to, sitesB, countB, cur6);
        }

        if (countB == 0)
            countB = ScanSiteB(pid, textStart, textEnd, sitesB, countB, patchB, cur6, io, cr3, dmapBase);

        bool patchApplied = countA != 0 || countB != 0;
        if (patchApplied)
        {
            PayloadCrt.Klog("sp:cpb:patched\n\0"u8);
            PayloadCrt.Klog("sp:cpb:resident\n\0"u8);
        }
        else
        {
            PayloadCrt.Klog("sp:cpb:not-found\n\0"u8);
        }

        
        
        
        
        
        RunPersistentWork(fw, patchApplied);

        if (!patchApplied)
            return 12;

        
        NotifyText("Thanks for using NoUpdate! Support my work on Ko-fi: ko-fi.com/zzer0dayyy"u8);

        
        while (true)
        {
            PayloadThread.sleep(5);

            for (int i = 0; i < countA; i++)
            {
                if (PayloadProcessMemory.Read(pid, (nint)sitesA[i], cur3, 3) != 0)
                {
                    PayloadCrt.Klog("sp:cpb:watch-a-lost\n\0"u8);
                    return 0;
                }
                if (!(cur3[0] == 0x45 && cur3[1] == 0x31 && cur3[2] == 0xFF))
                {
                    if (WriteSite(pid, sitesA[i], patchA, 3, io, cr3, dmapBase))
                        PayloadCrt.Klog("sp:cpb:a-reapplied\n\0"u8);
                    else
                        PayloadCrt.Klog("sp:cpb:a-reapply-fail\n\0"u8);
                }
            }

            for (int i = 0; i < countB; i++)
            {
                if (PayloadProcessMemory.Read(pid, (nint)sitesB[i], cur6, 6) != 0)
                {
                    PayloadCrt.Klog("sp:cpb:watch-b-lost\n\0"u8);
                    return 0;
                }
                if (!(cur6[0] == 0x45 && cur6[1] == 0x31 && cur6[2] == 0xFF &&
                      cur6[3] == 0x90 && cur6[4] == 0x90 && cur6[5] == 0x90))
                {
                    if (WriteSite(pid, sitesB[i], patchB, 6, io, cr3, dmapBase))
                        PayloadCrt.Klog("sp:cpb:b-reapplied\n\0"u8);
                    else
                        PayloadCrt.Klog("sp:cpb:b-reapply-fail\n\0"u8);
                }
            }
        }
    }

    private sealed class GameInfo
    {
        public byte[] TitleId = new byte[10];
        public byte[] Name = new byte[MaxNameBytes];
        public int NameLength;
        public byte[] ContentVersion = new byte[MaxVersionBytes];
        public int ContentVersionLength;
        public byte[] TargetVersion = new byte[MaxVersionBytes];
        public int TargetVersionLength;
        public uint RequiredFw;
        public int Flags;
        public int PersistFlags;
        public bool AlreadyBlocked;
    }

    
    
    
    private static int ScanGames(GameInfo[] games, uint fw, out int dirCount, out int paramCount, out bool ok)
    {
        dirCount = 0;
        paramCount = 0;
        ok = false;
        int gameCount = 0;

        byte* dirPath = stackalloc byte[20];
        WriteCString(dirPath, "/user/appmeta"u8);
        void* dir = PayloadFileSystem.opendir(dirPath);
        if (dir == null)
            return 0;

        ok = true;
        byte* paramPath = stackalloc byte[64];
        byte[] buffer = new byte[MaxParamBytes];

        while (true)
        {
            FreeBsdDirent* entry = PayloadFileSystem.readdir(dir);
            if (entry == null)
                break;
            if (entry->d_type != PayloadFileSystem.DT_DIR && entry->d_type != 0)
                continue;

            int nameLen = entry->d_namlen;
            if (nameLen == 0)
                nameLen = CStringLength(entry->d_name);
            if (!IsTitleId(entry->d_name, nameLen))
                continue;

            dirCount++;
            BuildParamPath(paramPath, entry->d_name);

            int length = ReadWholeFile(paramPath, buffer);
            if (length <= 0)
                continue;
            paramCount++;

            GameInfo? game = ParseGame(buffer.AsSpan(0, length), entry->d_name, fw);
            if (game != null && gameCount < games.Length)
                games[gameCount++] = game;
        }

        PayloadFileSystem.closedir(dir);
        return gameCount;
    }

    private static GameInfo? ParseGame(ReadOnlySpan<byte> json, byte* titleId, uint fw)
    {
        
        
        bool gameLike = true;
        if (TryFindNumber(json, "\"contentBadgeType\""u8, out long badge))
            gameLike &= badge == 1;
        if (TryFindNumber(json, "\"applicationCategoryType\""u8, out long category))
            gameLike &= category == 0;
        if (TryFindStringValue(json, "\"contentId\""u8, out ReadOnlySpan<byte> contentId))
        {
            if (StartsWith(contentId, "IV9999-"u8) || StartsWith(contentId, "UP0000-"u8))
                gameLike = false;
        }
        if (!gameLike)
            return null;

        var game = new GameInfo();
        for (int i = 0; i < 9; i++)
            game.TitleId[i] = titleId[i];
        game.TitleId[9] = 0;

        if (TryFindRawValue(json, "\"requiredSystemSoftwareVersion\""u8, out ReadOnlySpan<byte> requiredRaw))
            game.RequiredFw = (uint)(ParseUnsigned(requiredRaw) >> 32);

        if (TryFindStringValue(json, "\"contentVersion\""u8, out ReadOnlySpan<byte> contentVersion))
            game.ContentVersionLength = CopyBytes(contentVersion, game.ContentVersion);
        if (TryFindStringValue(json, "\"targetContentVersion\""u8, out ReadOnlySpan<byte> targetVersion))
            game.TargetVersionLength = CopyBytes(targetVersion, game.TargetVersion);

        if (game.RequiredFw != 0 && game.RequiredFw > fw)
            game.Flags |= FlagFw;
        bool versionSentinel = game.ContentVersionLength >= 3 &&
            game.ContentVersion[0] == (byte)'9' &&
            game.ContentVersion[1] == (byte)'9' &&
            game.ContentVersion[2] == (byte)'.';
        if (versionSentinel)
        {
            game.AlreadyBlocked = true;
            game.PersistFlags = PersistOk;
        }
        else if (game.TargetVersionLength != 0 &&
                 !BytesEqual(game.TargetVersion, game.TargetVersionLength,
                             game.ContentVersion, game.ContentVersionLength))
        {
            game.Flags |= FlagUpdate;
        }

        if (game.ContentVersionLength == 0 && game.RequiredFw == 0)
            return null;

        if (!TryGetTitleName(json, game.Name, out game.NameLength))
            game.NameLength = CopyBytes(titleId, 9, game.Name);
        return game;
    }

    private static bool TryGetTitleName(ReadOnlySpan<byte> json, byte[] destination, out int length)
    {
        length = 0;
        int localizedAt = FindBytes(json, 0, "\"localizedParameters\""u8);

        if (localizedAt >= 0 &&
            TryFindStringValue(json, "\"defaultLanguage\""u8, out ReadOnlySpan<byte> language))
        {
            int languageLength = language.Length;
            if (languageLength > 16)
                languageLength = 16;

            Span<byte> languageKey = stackalloc byte[languageLength + 2];
            languageKey[0] = (byte)'"';
            for (int i = 0; i < languageLength; i++)
                languageKey[i + 1] = language[i];
            languageKey[^1] = (byte)'"';

            int searchAt = localizedAt;
            int keyAt = -1;
            while (searchAt < json.Length)
            {
                int candidate = FindBytes(json, searchAt, languageKey);
                if (candidate < 0)
                    break;
                int after = candidate + languageKey.Length;
                int colonAt = after;
                while (colonAt < json.Length && IsJsonSpace(json[colonAt]))
                    colonAt++;
                if (colonAt < json.Length && json[colonAt] == (byte)':')
                {
                    keyAt = candidate;
                    break;
                }
                searchAt = after;
            }

            if (keyAt >= 0)
            {
                int titleAt = FindBytes(json, keyAt, "\"titleName\""u8);
                if (titleAt >= 0 &&
                    TryFindStringValue(json, "\"titleName\""u8, out ReadOnlySpan<byte> title, titleAt))
                {
                    length = DecodeJsonString(title, destination);
                    return length > 0;
                }
            }
        }

        int first = localizedAt >= 0 ? localizedAt : 0;
        if (TryFindStringValue(json, "\"titleName\""u8, out ReadOnlySpan<byte> fallback, first))
        {
            length = DecodeJsonString(fallback, destination);
            return length > 0;
        }
        return false;
    }

    
    
    
    private static bool TryFindRawValue(ReadOnlySpan<byte> json, ReadOnlySpan<byte> key,
        out ReadOnlySpan<byte> raw, int start = 0)
    {
        raw = default;
        if (start < 0 || start >= json.Length)
            return false;

        int keyAt = FindBytes(json, start, key);
        if (keyAt < 0)
            return false;

        int i = keyAt + key.Length;
        while (i < json.Length && IsJsonSpace(json[i])) i++;
        if (i >= json.Length || json[i] != (byte)':')
            return false;
        i++;
        while (i < json.Length && IsJsonSpace(json[i])) i++;
        if (i >= json.Length)
            return false;

        if (json[i] == (byte)'"')
        {
            int valueStart = i;
            i++;
            while (i < json.Length)
            {
                if (json[i] == (byte)'\\')
                {
                    i += 2;
                    continue;
                }
                if (json[i] == (byte)'"')
                {
                    i++;
                    raw = json[valueStart..i];
                    return true;
                }
                i++;
            }
            return false;
        }

        int tokenStart = i;
        while (i < json.Length && json[i] != (byte)',' && json[i] != (byte)'}' &&
               json[i] != (byte)']' && !IsJsonSpace(json[i]))
            i++;
        if (i == tokenStart)
            return false;
        raw = json[tokenStart..i];
        return true;
    }

    private static bool TryFindStringValue(ReadOnlySpan<byte> json, ReadOnlySpan<byte> key,
        out ReadOnlySpan<byte> value, int start = 0)
    {
        value = default;
        if (!TryFindRawValue(json, key, out ReadOnlySpan<byte> raw, start))
            return false;
        if (raw.Length < 2 || raw[0] != (byte)'"' || raw[^1] != (byte)'"')
            return false;
        value = raw[1..^1];
        return true;
    }

    private static bool TryFindNumber(ReadOnlySpan<byte> json, ReadOnlySpan<byte> key, out long value)
    {
        value = 0;
        if (!TryFindRawValue(json, key, out ReadOnlySpan<byte> raw))
            return false;
        if (raw.Length >= 2 && raw[0] == (byte)'"' && raw[^1] == (byte)'"')
            raw = raw[1..^1];

        bool negative = raw.Length > 0 && raw[0] == (byte)'-';
        int i = negative ? 1 : 0;
        long parsed = 0;
        bool any = false;
        for (; i < raw.Length; i++)
        {
            byte c = raw[i];
            if (c < (byte)'0' || c > (byte)'9')
                break;
            parsed = parsed * 10 + (c - (byte)'0');
            any = true;
        }
        if (!any)
            return false;
        value = negative ? -parsed : parsed;
        return true;
    }

    private static ulong ParseUnsigned(ReadOnlySpan<byte> raw)
    {
        if (raw.Length >= 2 && raw[0] == (byte)'"' && raw[^1] == (byte)'"')
            raw = raw[1..^1];

        bool hex = raw.Length >= 2 && raw[0] == (byte)'0' &&
                   (raw[1] == (byte)'x' || raw[1] == (byte)'X');
        if (hex)
            raw = raw[2..];

        ulong value = 0;
        bool any = false;
        for (int i = 0; i < raw.Length; i++)
        {
            int digit = hex ? HexDigit(raw[i]) : DecDigit(raw[i]);
            if (digit < 0)
                break;
            value = hex ? (value << 4) | (uint)digit : value * 10 + (uint)digit;
            any = true;
        }
        return any ? value : 0;
    }

    private static int DecodeJsonString(ReadOnlySpan<byte> raw, byte[] destination)
    {
        int max = destination.Length - 1;
        int n = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            byte c = raw[i];
            if (c == (byte)'\\' && i + 1 < raw.Length)
            {
                byte escaped = raw[++i];
                switch (escaped)
                {
                    case (byte)'"': c = (byte)'"'; break;
                    case (byte)'\\': c = (byte)'\\'; break;
                    case (byte)'/': c = (byte)'/'; break;
                    case (byte)'b': c = 8; break;
                    case (byte)'f': c = 12; break;
                    case (byte)'n': c = 10; break;
                    case (byte)'r': c = 13; break;
                    case (byte)'t': c = 9; break;
                    case (byte)'u':
                        if (i + 4 < raw.Length)
                        {
                            int code = 0;
                            bool valid = true;
                            for (int k = 0; k < 4; k++)
                            {
                                int digit = HexDigit(raw[++i]);
                                if (digit < 0) { valid = false; break; }
                                code = (code << 4) | digit;
                            }
                            if (valid)
                            {
                                if (code < 0x80)
                                {
                                    if (n < max) destination[n++] = (byte)code;
                                }
                                else if (code < 0x800)
                                {
                                    if (n + 1 < max)
                                    {
                                        destination[n++] = (byte)(0xC0 | (code >> 6));
                                        destination[n++] = (byte)(0x80 | (code & 0x3F));
                                    }
                                }
                                else
                                {
                                    if (n + 2 < max)
                                    {
                                        destination[n++] = (byte)(0xE0 | (code >> 12));
                                        destination[n++] = (byte)(0x80 | ((code >> 6) & 0x3F));
                                        destination[n++] = (byte)(0x80 | (code & 0x3F));
                                    }
                                }
                            }
                        }
                        continue;
                    default:
                        c = escaped;
                        break;
                }
            }

            if (c == 0 || c == (byte)'\n' || c == (byte)'\r')
                c = (byte)' ';
            if (n < max)
                destination[n++] = c;
        }
        destination[n] = 0;
        return n;
    }

    
    
    
    
    
    
    private static int PersistBlockedGames(GameInfo[] games, int gameCount, uint fw)
    {
        int changed = 0;
        byte* path = stackalloc byte[MaxPersistPathBytes];
        for (int i = 0; i < gameCount; i++)
        {
            ref GameInfo game = ref games[i];
            if (game.Flags == 0)
                continue;

            bool any = false;
            bool ok = true;
            fixed (byte* titleId = game.TitleId)
            {
                BuildPersistPath(path, "/user/appmeta/"u8, titleId, "/param.json\0"u8);
                PersistParamFile(path, titleId, "appmeta"u8, fw, game.Flags, ref any, ref ok);

                BuildPersistPath(path, "/user/app/"u8, titleId, "/sce_sys/param.json\0"u8);
                PersistParamFile(path, titleId, "app"u8, fw, game.Flags, ref any, ref ok);

                BuildPersistPath(path, "/user/patch/"u8, titleId, "/sce_sys/param.json\0"u8);
                PersistParamFile(path, titleId, "patch"u8, fw, game.Flags, ref any, ref ok);
            }

            if (any && ok)
            {
                game.PersistFlags = PersistOk;
                changed++;
            }
            else if (any)
            {
                game.PersistFlags = PersistFail;
            }
        }
        return changed;
    }

    private static void PersistParamFile(byte* path, byte* titleId, ReadOnlySpan<byte> tag,
        uint fw, int flags, ref bool any, ref bool ok)
    {
        byte[] buffer = new byte[MaxParamFileBytes];
        int length = ReadWholeFile(path, buffer);
        if (length <= 0)
            return;

        ReadOnlySpan<byte> json = buffer.AsSpan(0, length);
        int fwAt = FindJsonStringValueOffset(json, "\"requiredSystemSoftwareVersion\""u8, 18);
        int verAt = FindJsonStringValueOffset(json, "\"contentVersion\""u8, 10);
        int targetAt = FindJsonStringValueOffset(json, "\"targetContentVersion\""u8, 10);

        bool needFw = (flags & FlagFw) != 0 && fwAt >= 0;
        
        
        
        bool needVer = EnableContentVersionRewrite &&
            (flags & FlagUpdate) != 0 && (flags & FlagFw) == 0 &&
            verAt >= 0 && targetAt >= 0;
        
        if (needVer && json[verAt] == (byte)'9' && json[verAt + 1] == (byte)'9' &&
            json[verAt + 2] == (byte)'.')
            needVer = false;
        if (!needFw && !needVer)
            return;

        if (!BackupFile(path, titleId, tag))
        {
            any = true;
            ok = false;
            KlogPersistFail(titleId, tag);
            return;
        }

        int fd = PayloadIo.open(path, PayloadFileSystem.O_WRONLY);
        if (fd < 0)
        {
            any = true;
            ok = false;
            KlogPersistFail(titleId, tag);
            return;
        }

        byte* newFw = stackalloc byte[18];
        byte* newVer = stackalloc byte[10];
        if (needFw)
        {
            BuildFwString(newFw, fw);
            long written = PayloadIo.pwrite(fd, newFw, 18, fwAt);
            if (written != 18)
                ok = false;
        }

        if (needVer)
        {
            ReadOnlySpan<byte> sentinel = "99.999.999"u8;
            for (int i = 0; i < 10; i++)
                newVer[i] = sentinel[i];
            long written = PayloadIo.pwrite(fd, newVer, 10, verAt);
            if (written != 10)
                ok = false;
        }

        PayloadIo.close(fd);
        any = true;

        if (needFw && !VerifyAt(path, fwAt, newFw, 18))
            ok = false;
        if (needVer && !VerifyAt(path, verAt, newVer, 10))
            ok = false;

        if (ok)
            KlogPersistOk(titleId, tag, needFw, fw, needVer);
        else
            KlogPersistFail(titleId, tag);
    }

    private static int FindJsonStringValueOffset(ReadOnlySpan<byte> json, ReadOnlySpan<byte> key, int expectedLength)
    {
        int keyAt = FindBytes(json, 0, key);
        if (keyAt < 0)
            return -1;

        int i = keyAt + key.Length;
        while (i < json.Length && IsJsonSpace(json[i])) i++;
        if (i >= json.Length || json[i] != (byte)':')
            return -1;
        i++;
        while (i < json.Length && IsJsonSpace(json[i])) i++;
        if (i >= json.Length || json[i] != (byte)'"')
            return -1;
        i++;
        if (i + expectedLength >= json.Length)
            return -1;
        if (json[i + expectedLength] != (byte)'"')
            return -1;
        return i;
    }

    private static void BuildFwString(byte* dst, uint fw)
    {
        ReadOnlySpan<byte> hex = "0123456789abcdef"u8;
        dst[0] = (byte)'0';
        dst[1] = (byte)'x';
        ulong value = ((ulong)fw) << 32;
        for (int i = 0; i < 16; i++)
        {
            int shift = (15 - i) * 4;
            dst[2 + i] = hex[(int)((value >> shift) & 0xF)];
        }
    }

    private static void BuildPersistPath(byte* dst, ReadOnlySpan<byte> prefix, byte* titleId, ReadOnlySpan<byte> suffix)
    {
        int n = 0;
        for (int i = 0; i < prefix.Length; i++)
            dst[n++] = prefix[i];
        for (int i = 0; i < 9; i++)
            dst[n++] = titleId[i];
        for (int i = 0; i < suffix.Length; i++)
            dst[n++] = suffix[i];
    }

    private static void BuildBackupDir(byte* dst, byte* titleId)
    {
        ReadOnlySpan<byte> prefix = "/data/NoUpdate/backup/"u8;
        int n = 0;
        for (int i = 0; i < prefix.Length; i++)
            dst[n++] = prefix[i];
        for (int i = 0; i < 9; i++)
            dst[n++] = titleId[i];
        dst[n] = 0;
    }

    private static void BuildBackupPath(byte* dst, byte* titleId, ReadOnlySpan<byte> tag)
    {
        BuildBackupDir(dst, titleId);
        int n = CStringLength(dst);
        dst[n++] = (byte)'/';
        for (int i = 0; i < tag.Length; i++)
            dst[n++] = tag[i];
        ReadOnlySpan<byte> suffix = ".param.json\0"u8;
        for (int i = 0; i < suffix.Length; i++)
            dst[n++] = suffix[i];
    }

    private static bool BackupFile(byte* path, byte* titleId, ReadOnlySpan<byte> tag)
    {
        Mkdir("/data/NoUpdate\0"u8);
        Mkdir("/data/NoUpdate/backup\0"u8);

        byte* dir = stackalloc byte[64];
        BuildBackupDir(dir, titleId);
        PayloadFileSystem.mkdir(dir, 0x1FF);

        byte* backup = stackalloc byte[MaxPersistPathBytes];
        BuildBackupPath(backup, titleId, tag);
        if (PayloadFileSystem.access(backup, 0) == 0)
            return true;
        return CopyFile(path, backup);
    }

    private static bool CopyFile(byte* src, byte* dst)
    {
        int inFd = PayloadIo.open(src, PayloadFileSystem.O_RDONLY);
        if (inFd < 0)
            return false;

        int outFd = PayloadIo.open(dst,
            PayloadFileSystem.O_WRONLY | PayloadFileSystem.O_CREAT | PayloadFileSystem.O_TRUNC,
            (ushort)0x1A4);
        if (outFd < 0)
        {
            PayloadIo.close(inFd);
            return false;
        }

        byte[] buffer = new byte[4096];
        bool ok = true;
        fixed (byte* p = buffer)
        {
            while (true)
            {
                long n = PayloadIo.read(inFd, p, (nuint)buffer.Length);
                if (n < 0)
                {
                    ok = false;
                    break;
                }
                if (n == 0)
                    break;
                long written = PayloadIo.write(outFd, p, (nuint)n);
                if (written != n)
                {
                    ok = false;
                    break;
                }
            }
        }

        PayloadIo.close(inFd);
        PayloadIo.close(outFd);
        return ok;
    }

    private static void KlogPersistOk(byte* titleId, ReadOnlySpan<byte> tag, bool fwChanged, uint fw, bool verChanged)
    {
        byte[] line = new byte[128];
        int n = 0;
        n = Append(line, n, "sp:cpb:persist "u8);
        for (int i = 0; i < 9; i++)
            n = AppendByte(line, n, titleId[i]);
        n = AppendByte(line, n, (byte)' ');
        n = Append(line, n, tag);
        n = Append(line, n, " ok"u8);
        if (fwChanged)
        {
            n = Append(line, n, " fw="u8);
            n = AppendBcd(line, n, fw);
        }
        if (verChanged)
            n = Append(line, n, " ver=99.999.999"u8);
        n = AppendByte(line, n, (byte)'\n');
        KlogLine(line, n);
    }

    private static void KlogPersistFail(byte* titleId, ReadOnlySpan<byte> tag)
    {
        byte[] line = new byte[96];
        int n = 0;
        n = Append(line, n, "sp:cpb:persist "u8);
        for (int i = 0; i < 9; i++)
            n = AppendByte(line, n, titleId[i]);
        n = AppendByte(line, n, (byte)' ');
        n = Append(line, n, tag);
        n = Append(line, n, " fail\n"u8);
        KlogLine(line, n);
    }

    private static void ReportGames(GameInfo[] games, int gameCount, bool scanOk, bool patchApplied, int persisted)
    {
        int forceCount = 0;
        int blockedCount = 0;
        for (int i = 0; i < gameCount; i++)
        {
            if (games[i].Flags != 0)
                forceCount++;
            else if (games[i].AlreadyBlocked)
                blockedCount++;
        }

        int reportCount = gameCount;

        byte[] line = new byte[320];
        int n = 0;
        n = Append(line, n, "sp:cpb:titles "u8);
        n = AppendDecimal(line, n, gameCount);
        n = Append(line, n, " force="u8);
        n = AppendDecimal(line, n, forceCount);
        n = Append(line, n, " blocked="u8);
        n = AppendDecimal(line, n, blockedCount);
        n = Append(line, n, " persisted="u8);
        n = AppendDecimal(line, n, persisted);
        n = Append(line, n, "\n"u8);
        KlogLine(line, n);

        for (int i = 0; i < gameCount; i++)
        {
            GameInfo game = games[i];
            n = 0;
            n = Append(line, n, "sp:cpb:title "u8);
            n = Append(line, n, game.TitleId.AsSpan(0, 9));
            n = Append(line, n, " req="u8);
            n = AppendBcd(line, n, game.RequiredFw);
            n = Append(line, n, " ver="u8);
            n = Append(line, n, game.ContentVersion.AsSpan(0, game.ContentVersionLength));
            n = Append(line, n, " flags="u8);
            if (game.Flags == 0)
            {
                n = AppendByte(line, n, game.AlreadyBlocked ? (byte)'B' : (byte)'0');
            }
            else
            {
                if ((game.Flags & FlagFw) != 0)
                    n = Append(line, n, "FW"u8);
                if ((game.Flags & FlagUpdate) != 0)
                {
                    if ((game.Flags & FlagFw) != 0)
                        n = AppendByte(line, n, (byte)'+');
                    n = Append(line, n, "UPD"u8);
                }
            }
            n = Append(line, n, " persist="u8);
            if ((game.PersistFlags & PersistOk) != 0)
                n = Append(line, n, "ok"u8);
            else if ((game.PersistFlags & PersistFail) != 0)
                n = Append(line, n, "fail"u8);
            else
                n = Append(line, n, "none"u8);
            n = AppendByte(line, n, (byte)' ');
            n = Append(line, n, game.Name.AsSpan(0, game.NameLength));
            n = Append(line, n, "\n"u8);
            KlogLine(line, n);
        }

        if (!scanOk)
        {
            NotifyText("ZER0DAY: game list unreadable"u8);
            return;
        }

        if (!patchApplied)
            NotifyText("ZER0DAY: global patch NOT applied"u8);

        if (reportCount == 0)
        {
            NotifyText("ZER0DAY: no games found"u8);
            return;
        }

        n = 0;
        n = Append(line, n, "ZER0DAY: games found - "u8);
        n = AppendDecimal(line, n, reportCount);
        n = Append(line, n, " games"u8);
        NotifyLine(line, n);

        for (int i = 0; i < gameCount; i++)
        {
            GameInfo game = games[i];
            n = 0;
            n = Append(line, n, "ZER0DAY "u8);
            n = Append(line, n, game.TitleId.AsSpan(0, 9));
            n = AppendByte(line, n, (byte)' ');
            int nameLength = game.NameLength;
            if (nameLength > 40)
                nameLength = 40;
            n = Append(line, n, game.Name.AsSpan(0, nameLength));
            n = Append(line, n, " - "u8);
            if ((game.Flags & FlagFw) != 0)
            {
                n = Append(line, n, "FW "u8);
                n = AppendBcd(line, n, game.RequiredFw);
            }
            else if ((game.Flags & FlagUpdate) != 0)
            {
                n = Append(line, n, "update"u8);
            }
            else if (game.AlreadyBlocked)
            {
                n = Append(line, n, "blocked"u8);
            }
            else
            {
                n = Append(line, n, "ok"u8);
            }
            NotifyLine(line, n);
        }

        n = 0;
        n = Append(line, n, "ZER0DAY: "u8);
        n = AppendDecimal(line, n, reportCount);
        n = Append(line, n, " games listed"u8);
        NotifyLine(line, n);
    }

    private static void RunPersistentWork(uint fw, bool patchApplied)
    {
        int dnsResult = ApplyNomadicDns();
        ReportDns(dnsResult);

        var games = new GameInfo[MaxGames];
        int gameCount = ScanGames(games, fw, out int scannedDirs, out int scannedParams, out bool scanOk);
        KlogScanLine(scannedDirs, scannedParams, gameCount, scanOk);

        int persisted = PersistBlockedGames(games, gameCount, fw);
        ReportGames(games, gameCount, scanOk, patchApplied, persisted);
    }

    private static void KlogLine(byte[] buffer, int length)
    {
        if (length >= buffer.Length)
            length = buffer.Length - 1;
        buffer[length] = 0;
        PayloadCrt.Klog(buffer.AsSpan(0, length + 1));
    }

    private static void NotifyLine(byte[] buffer, int length)
    {
        int rc = PayloadNotification.SendKernelNotification(buffer.AsSpan(0, length));
        if (rc != 0)
            KlogHex("sp:cpb:notify-fail "u8, (ulong)(uint)rc);
        PayloadThread.usleep(250_000);
    }

    private static void NotifyText(ReadOnlySpan<byte> text)
    {
        int rc = PayloadNotification.SendKernelNotification(text);
        if (rc != 0)
            KlogHex("sp:cpb:notify-fail "u8, (ulong)(uint)rc);
        PayloadThread.usleep(250_000);
    }

    private static int Append(byte[] destination, int position, ReadOnlySpan<byte> source)
    {
        int count = source.Length;
        if (position + count > destination.Length)
            count = destination.Length - position;
        if (count < 0)
            count = 0;
        for (int i = 0; i < count; i++)
            destination[position + i] = source[i];
        return position + count;
    }

    private static int AppendByte(byte[] destination, int position, byte value)
    {
        if (position < destination.Length)
        {
            destination[position] = value;
            return position + 1;
        }
        return position;
    }

    private static int AppendDecimal(byte[] destination, int position, int value)
    {
        if (value < 0)
            value = 0;
        if (value == 0)
            return AppendByte(destination, position, (byte)'0');

        Span<byte> digits = stackalloc byte[10];
        int count = 0;
        while (value > 0)
        {
            digits[count++] = (byte)('0' + value % 10);
            value /= 10;
        }
        while (count > 0)
            position = AppendByte(destination, position, digits[--count]);
        return position;
    }

    private static int AppendBcd(byte[] destination, int position, uint value)
    {
        int majorByte = (int)((value >> 24) & 0xFF);
        int minorByte = (int)((value >> 16) & 0xFF);
        int major = (majorByte >> 4) * 10 + (majorByte & 0xF);
        int minor = (minorByte >> 4) * 10 + (minorByte & 0xF);

        position = AppendDecimal(destination, position, major);
        position = AppendByte(destination, position, (byte)'.');
        if (minor < 10)
            position = AppendByte(destination, position, (byte)'0');
        return AppendDecimal(destination, position, minor);
    }

    private static bool VerifyAt(byte* path, int offset, byte* expected, int length)
    {
        byte* actual = stackalloc byte[18];
        int fd = PayloadIo.open(path, PayloadFileSystem.O_RDONLY);
        if (fd < 0)
            return false;
        long n = PayloadIo.pread(fd, actual, (nuint)length, offset);
        PayloadIo.close(fd);
        if (n != length)
            return false;
        for (int i = 0; i < length; i++)
        {
            if (actual[i] != expected[i])
                return false;
        }
        return true;
    }

    private static int Mkdir(ReadOnlySpan<byte> path)
    {
        byte* p = stackalloc byte[path.Length];
        for (int i = 0; i < path.Length; i++)
            p[i] = path[i];
        return PayloadFileSystem.mkdir(p, 0x1FF);
    }

    private static int ReadWholeFile(byte* path, byte[] buffer)
    {
        int fd = PayloadIo.open(path, PayloadFileSystem.O_RDONLY);
        if (fd < 0)
            return 0;

        int total = 0;
        fixed (byte* p = buffer)
        {
            while (total < buffer.Length)
            {
                long n = PayloadIo.read(fd, p + total, (nuint)(buffer.Length - total));
                if (n <= 0)
                    break;
                total += (int)n;
            }
        }
        PayloadIo.close(fd);
        return total;
    }

    private static void BuildParamPath(byte* dst, byte* titleId)
    {
        ReadOnlySpan<byte> prefix = "/user/appmeta/"u8;
        for (int i = 0; i < prefix.Length; i++)
            dst[i] = prefix[i];

        int n = prefix.Length;
        for (int i = 0; i < 9; i++)
            dst[n++] = titleId[i];

        ReadOnlySpan<byte> suffix = "/param.json\0"u8;
        for (int i = 0; i < suffix.Length; i++)
            dst[n++] = suffix[i];
    }

    private static void WriteCString(byte* dst, ReadOnlySpan<byte> src)
    {
        for (int i = 0; i < src.Length; i++)
            dst[i] = src[i];
        dst[src.Length] = 0;
    }

    private static int CStringLength(byte* s)
    {
        int n = 0;
        while (s[n] != 0)
            n++;
        return n;
    }

    private static bool IsTitleId(byte* name, int length)
    {
        if (length != 9)
            return false;
        for (int i = 0; i < 4; i++)
            if (name[i] < (byte)'A' || name[i] > (byte)'Z')
                return false;
        for (int i = 4; i < 9; i++)
            if (name[i] < (byte)'0' || name[i] > (byte)'9')
                return false;
        return true;
    }

    private static int FindBytes(ReadOnlySpan<byte> haystack, int start, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0)
            return start <= haystack.Length ? start : -1;
        if (start < 0)
            start = 0;
        int limit = haystack.Length - needle.Length;
        for (int i = start; i <= limit; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
                j++;
            if (j == needle.Length)
                return i;
        }
        return -1;
    }

    private static bool StartsWith(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix)
    {
        if (value.Length < prefix.Length)
            return false;
        for (int i = 0; i < prefix.Length; i++)
            if (value[i] != prefix[i])
                return false;
        return true;
    }

    private static bool BytesEqual(byte[] left, int leftLength, byte[] right, int rightLength)
    {
        if (leftLength != rightLength)
            return false;
        for (int i = 0; i < leftLength; i++)
            if (left[i] != right[i])
                return false;
        return true;
    }

    private static int CopyBytes(ReadOnlySpan<byte> source, byte[] destination)
    {
        int max = destination.Length - 1;
        int count = source.Length < max ? source.Length : max;
        for (int i = 0; i < count; i++)
            destination[i] = source[i];
        destination[count] = 0;
        return count;
    }

    private static int CopyBytes(byte* source, int length, byte[] destination)
    {
        int max = destination.Length - 1;
        int count = length < max ? length : max;
        for (int i = 0; i < count; i++)
            destination[i] = source[i];
        destination[count] = 0;
        return count;
    }

    private static bool IsJsonSpace(byte c)
        => c == (byte)' ' || c == (byte)'\t' || c == (byte)'\r' || c == (byte)'\n';

    private static int HexDigit(byte c)
    {
        if (c >= (byte)'0' && c <= (byte)'9') return c - (byte)'0';
        if (c >= (byte)'a' && c <= (byte)'f') return c - (byte)'a' + 10;
        if (c >= (byte)'A' && c <= (byte)'F') return c - (byte)'A' + 10;
        return -1;
    }

    private static int DecDigit(byte c)
        => c >= (byte)'0' && c <= (byte)'9' ? c - (byte)'0' : -1;

    private static void KlogScanLine(int dirCount, int paramCount, int gameCount, bool scanOk)
    {
        if (!scanOk)
        {
            PayloadCrt.Klog("sp:cpb:scan:opendir-fail\n\0"u8);
            return;
        }

        var line = new byte[128];
        int n = 0;
        n = Append(line, n, "sp:cpb:scan dirs="u8);
        n = AppendDecimal(line, n, dirCount);
        n = Append(line, n, " params="u8);
        n = AppendDecimal(line, n, paramCount);
        n = Append(line, n, " titles="u8);
        n = AppendDecimal(line, n, gameCount);
        n = Append(line, n, "\n"u8);
        KlogLine(line, n);
    }

    
    
    private static int ScanSiteB(int pid, ulong from, ulong to,
        ulong* sitesB, int countB, byte* patchB, byte* cur6,
        PayloadKernelIo io, ulong cr3, ulong dmapBase)
    {
        byte[] immB = { 0x3C, 0x00, 0xA4, 0x80 };
        byte[] maskB = { 0xFF, 0xFF, 0xFF, 0xFF };

        ulong cursor = from;
        while (cursor + 2 < to && countB < MaxSitesB)
        {
            nint m = PayloadProcessMemory.PatternScan(
                pid, (nint)cursor, (nuint)(to - cursor), immB, maskB);
            if (m == 0)
                break;

            ulong site = (ulong)m - 2;
            if (site >= from && PayloadProcessMemory.Read(pid, (nint)site, cur6, 6) == 0)
            {
                if (cur6[0] == 0x41 && cur6[1] == 0xBF && cur6[2] == 0x3C &&
                    cur6[3] == 0x00 && cur6[4] == 0xA4 && cur6[5] == 0x80)
                {
                    if (WriteSite(pid, site, patchB, 6, io, cr3, dmapBase) &&
                        PayloadProcessMemory.Read(pid, (nint)site, cur6, 6) == 0 &&
                        cur6[0] == 0x45 && cur6[1] == 0x31 && cur6[2] == 0xFF &&
                        cur6[3] == 0x90 && cur6[4] == 0x90 && cur6[5] == 0x90)
                    {
                        if (!Contains(sitesB, countB, site))
                            sitesB[countB++] = site;
                        KlogHex("sp:cpb:b-patched "u8, site);
                    }
                    else
                    {
                        PayloadCrt.Klog("sp:cpb:b-write-fail\n\0"u8);
                    }
                }
                else if (cur6[0] == 0x45 && cur6[1] == 0x31 && cur6[2] == 0xFF &&
                         cur6[3] == 0x90 && cur6[4] == 0x90 && cur6[5] == 0x90 &&
                         !Contains(sitesB, countB, site))
                {
                    sitesB[countB++] = site;
                    KlogHex("sp:cpb:b-already "u8, site);
                }
            }

            cursor = (ulong)m + (ulong)immB.Length;
        }

        return countB;
    }

    
    
    private static int ScanPatchedB(int pid, ulong from, ulong to,
        ulong* sitesB, int countB, byte* cur6)
    {
        byte[] patched = { 0x45, 0x31, 0xFF, 0x90, 0x90, 0x90 };
        byte[] mask = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        ulong cursor = from;
        while (cursor + 6 <= to && countB < MaxSitesB)
        {
            nint m = PayloadProcessMemory.PatternScan(
                pid, (nint)cursor, (nuint)(to - cursor), patched, mask);
            if (m == 0)
                break;

            ulong site = (ulong)m;
            if (PayloadProcessMemory.Read(pid, (nint)site, cur6, 6) == 0 &&
                cur6[0] == 0x45 && cur6[1] == 0x31 && cur6[2] == 0xFF &&
                cur6[3] == 0x90 && cur6[4] == 0x90 && cur6[5] == 0x90 &&
                !Contains(sitesB, countB, site))
            {
                sitesB[countB++] = site;
                KlogHex("sp:cpb:b-already "u8, site);
            }

            cursor = site + 6;
        }

        return countB;
    }

    private static bool Contains(ulong* values, int count, ulong value)
    {
        for (int i = 0; i < count; i++)
        {
            if (values[i] == value)
                return true;
        }
        return false;
    }

    
    
    
    
    
    private static int ApplyNomadicDns()
    {
        void* handle;
        fixed (byte* path = "/system/common/lib/libSceRegMgr.sprx\0"u8)
            handle = PayloadDlfcn.Dlopen(path, PayloadDlfcn.RtldNow);
        if (handle == null)
        {
            fixed (byte* soname = "libSceRegMgr.sprx\0"u8)
                handle = PayloadDlfcn.Dlopen(soname, PayloadDlfcn.RtldNow);
        }
        if (handle == null)
        {
            PayloadCrt.Klog("sp:cpb:dns-nolib\n\0"u8);
            return -1;
        }

        void* getStrSym = RegSym(handle, "sceRegMgrGetStr\0"u8);
        void* setStrSym = RegSym(handle, "sceRegMgrSetStr\0"u8);
        void* getIntSym = RegSym(handle, "sceRegMgrGetInt\0"u8);
        void* setIntSym = RegSym(handle, "sceRegMgrSetInt\0"u8);
        if (getStrSym == null || setStrSym == null || getIntSym == null || setIntSym == null)
        {
            PayloadCrt.Klog("sp:cpb:dns-nosym\n\0"u8);
            PayloadDlfcn.Dlclose(handle);
            return -1;
        }

        var getStr = (delegate* unmanaged<uint, byte*, ulong, int>)getStrSym;
        var setStr = (delegate* unmanaged<uint, byte*, ulong, int>)setStrSym;
        var getInt = (delegate* unmanaged<uint, int*, int>)getIntSym;
        var setInt = (delegate* unmanaged<uint, int, int>)setIntSym;

        int changed = 0;
        int already = 0;
        int failed = 0;

        
        
        int profile = 0;
        int profileRc = getInt(RegNetCurrentProfile, &profile);
        KlogDnsProfile(profileRc, profile);
        if (profileRc == 0 && profile >= 1 && profile <= 10)
        {
            uint profileBase = RegNetProfileDnsFlag + (uint)(profile - 1) * RegNetProfileStride;
            ApplyDnsProfile(getStr, setStr, getInt, setInt,
                profileBase, profileBase + 1, profileBase + 2,
                "prof"u8, ref changed, ref already, ref failed);
        }

        ApplyDnsProfile(getStr, setStr, getInt, setInt,
            RegNetIpDnsFlag, RegNetIpPrimaryDns, RegNetIpSecondaryDns,
            "eth"u8, ref changed, ref already, ref failed);
        ApplyDnsProfile(getStr, setStr, getInt, setInt,
            RegWifiIpDnsFlag, RegWifiIpPrimaryDns, RegWifiIpSecondaryDns,
            "wifi"u8, ref changed, ref already, ref failed);

        PayloadDlfcn.Dlclose(handle);
        if (changed > 0)
            return 1;
        if (already > 0)
            return 0;
        return -1;
    }

    private static void* RegSym(void* handle, ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
            return PayloadDlfcn.Dlsym(handle, p);
    }

    private static void ApplyDnsProfile(
        delegate* unmanaged<uint, byte*, ulong, int> getStr,
        delegate* unmanaged<uint, byte*, ulong, int> setStr,
        delegate* unmanaged<uint, int*, int> getInt,
        delegate* unmanaged<uint, int, int> setInt,
        uint flagId, uint primaryId, uint secondaryId,
        ReadOnlySpan<byte> tag, ref int changed, ref int already, ref int failed)
    {
        
        const int FieldSize = 16;

        byte* oldPrimary = stackalloc byte[FieldSize];
        byte* oldSecondary = stackalloc byte[FieldSize];
        for (int i = 0; i < FieldSize; i++)
        {
            oldPrimary[i] = 0;
            oldSecondary[i] = 0;
        }

        int flag = 0;
        if (getStr(primaryId, oldPrimary, FieldSize) != 0)
        {
            KlogDns(tag, "no-key"u8);
            return;
        }

        int flagReadRc = getInt(flagId, &flag);
        if (flagReadRc != 0)
        {
            flag = 0;
            KlogDns(tag, "no-flag"u8);
        }

        getStr(secondaryId, oldSecondary, FieldSize);
        KlogDnsOld(tag, oldPrimary, oldSecondary, flag);

        
        
        
        bool empty = oldPrimary[0] == 0;
        if (!empty && !LooksLikeIpv4(oldPrimary))
        {
            KlogDns(tag, "no-value"u8);
            return;
        }

        if (!empty && BytesEqualAscii(oldPrimary, "62.210.38.117"u8) && flag == 1)
        {
            KlogDns(tag, "already"u8);
            already++;
            return;
        }

        
        
        
        bool flagChanged = flag == 0;
        int flagRc = 0;
        if (flagChanged)
        {
            flagRc = SetRegInt(setInt, flagId, 1);
            if (flagRc != 0)
            {
                KlogDnsSet(tag, -1, flagRc, true);
                KlogDns(tag, "flag-fail"u8);
            }
            else
            {
                PayloadThread.usleep(100_000);
            }
        }

        int primaryRc = SetRegString(setStr, primaryId, "62.210.38.117\0"u8);
        KlogDnsSet(tag, primaryRc, flagRc, flagChanged);
        if (primaryRc != 0)
        {
            KlogDns(tag, "write-fail"u8);
            failed++;
            return;
        }

        
        
        

        byte* verify = stackalloc byte[FieldSize];
        int verifyFlag = 0;
        int getStrRc = -1;
        int getIntRc = -1;
        bool match = false;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            for (int i = 0; i < FieldSize; i++)
                verify[i] = 0;
            getStrRc = getStr(primaryId, verify, FieldSize);
            getIntRc = getInt(flagId, &verifyFlag);
            match = getStrRc == 0 && BytesEqualAscii(verify, "62.210.38.117"u8);
            if (match)
                break;
            PayloadThread.usleep(200_000);
        }

        if (!match)
        {
            KlogDnsVerify(tag, getStrRc, verify, getIntRc, verifyFlag);
            KlogDns(tag, "verify-fail"u8);
            failed++;
            return;
        }

        KlogDns(tag, "ok"u8);
        changed++;
    }

    private static int SetRegString(
        delegate* unmanaged<uint, byte*, ulong, int> setStr, uint id, ReadOnlySpan<byte> value)
    {
        
        
        const int FieldSize = 16;
        byte* buf = stackalloc byte[FieldSize];
        for (int i = 0; i < FieldSize; i++)
            buf[i] = 0;

        int count = value.Length;
        if (count > 0 && value[count - 1] == 0)
            count--; 
        if (count > FieldSize - 1)
            count = FieldSize - 1;
        for (int i = 0; i < count; i++)
            buf[i] = value[i];

        int rc = setStr(id, buf, (ulong)(count + 1));
        if (rc != 0)
            rc = setStr(id, buf, FieldSize);
        return rc;
    }

    private static int SetRegInt(
        delegate* unmanaged<uint, int, int> setInt, uint id, int value)
        => setInt(id, value);

    private static bool LooksLikeIpv4(byte* s)
    {
        int len = 0;
        int dots = 0;
        while (len < 16 && s[len] != 0)
        {
            byte c = s[len];
            if (c == (byte)'.')
                dots++;
            else if (c < (byte)'0' || c > (byte)'9')
                return false;
            len++;
        }
        return len >= 7 && len <= 15 && dots == 3;
    }

    private static bool BytesEqualAscii(byte* s, ReadOnlySpan<byte> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (s[i] != text[i])
                return false;
        }
        return s[text.Length] == 0;
    }

    private static void KlogDns(ReadOnlySpan<byte> tag, ReadOnlySpan<byte> status)
    {
        byte[] line = new byte[96];
        int n = 0;
        n = Append(line, n, "sp:cpb:dns "u8);
        n = Append(line, n, tag);
        n = AppendByte(line, n, (byte)' ');
        n = Append(line, n, status);
        n = AppendByte(line, n, (byte)'\n');
        KlogLine(line, n);
    }

    private static void KlogDnsProfile(int rc, int profile)
    {
        byte[] line = new byte[96];
        int n = 0;
        n = Append(line, n, "sp:cpb:dns profile rc="u8);
        n = AppendHex32(line, n, (uint)rc);
        n = Append(line, n, " id="u8);
        n = AppendDecimal(line, n, profile);
        n = AppendByte(line, n, (byte)'\n');
        KlogLine(line, n);
    }

    private static void KlogDnsSet(ReadOnlySpan<byte> tag, int strRc, int intRc, bool flagChanged)
    {
        byte[] line = new byte[128];
        int n = 0;
        n = Append(line, n, "sp:cpb:dns "u8);
        n = Append(line, n, tag);
        n = Append(line, n, " set str="u8);
        n = AppendHex32(line, n, (uint)strRc);
        n = Append(line, n, " flag="u8);
        n = AppendHex32(line, n, (uint)intRc);
        if (!flagChanged)
            n = Append(line, n, " (kept)"u8);
        n = AppendByte(line, n, (byte)'\n');
        KlogLine(line, n);
    }

    private static void KlogDnsVerify(ReadOnlySpan<byte> tag, int strRc, byte* value, int intRc, int flag)
    {
        byte[] line = new byte[224];
        int n = 0;
        n = Append(line, n, "sp:cpb:dns "u8);
        n = Append(line, n, tag);
        n = Append(line, n, " verify str="u8);
        n = AppendHex32(line, n, (uint)strRc);
        n = Append(line, n, " flag="u8);
        n = AppendHex32(line, n, (uint)intRc);
        n = AppendByte(line, n, (byte)'/');
        n = AppendDecimal(line, n, flag);
        n = Append(line, n, " v="u8);
        n = AppendDnsValue(line, n, value);
        n = Append(line, n, " hex="u8);
        for (int i = 0; i < 16; i++)
            n = AppendHexByte(line, n, value[i]);
        n = AppendByte(line, n, (byte)'\n');
        KlogLine(line, n);
    }

    private static int AppendHex32(byte[] line, int n, uint value)
    {
        for (int shift = 28; shift >= 0; shift -= 4)
            n = AppendHexByte(line, n, (byte)((value >> shift) & 0xF));
        return n;
    }

    private static int AppendHexByte(byte[] line, int n, byte value)
    {
        int hi = value >> 4;
        int lo = value & 0xF;
        n = AppendByte(line, n, (byte)(hi < 10 ? '0' + hi : 'a' + hi - 10));
        return AppendByte(line, n, (byte)(lo < 10 ? '0' + lo : 'a' + lo - 10));
    }

    private static void KlogDnsOld(ReadOnlySpan<byte> tag, byte* primary, byte* secondary, int flag)
    {
        byte[] line = new byte[192];
        int n = 0;
        n = Append(line, n, "sp:cpb:dns "u8);
        n = Append(line, n, tag);
        n = Append(line, n, " old="u8);
        n = AppendDnsValue(line, n, primary);
        n = Append(line, n, " sec="u8);
        n = AppendDnsValue(line, n, secondary);
        n = Append(line, n, " flag="u8);
        n = AppendDecimal(line, n, flag);
        n = AppendByte(line, n, (byte)'\n');
        KlogLine(line, n);
    }

    private static int AppendDnsValue(byte[] line, int n, byte* value)
    {
        
        int len = 0;
        while (len < 16 && value[len] != 0)
            len++;
        if (len == 0)
            return Append(line, n, "empty"u8);
        for (int i = 0; i < len; i++)
            n = AppendByte(line, n, value[i]);
        return n;
    }

    private static void ReportDns(int result)
    {
        if (result > 0)
            NotifyText("ZER0DAY: Nomadic DNS set - reboot the PS5"u8);
        else if (result == 0)
            NotifyText("ZER0DAY: Nomadic DNS already active"u8);
        else
            NotifyText("ZER0DAY: DNS not changed - see klog"u8);
    }

    
    
    
    
    private static void DisableAutostart()
    {
        RemoveAutostartAt("/user/data/OnionHEN/payloads/NoUpdate.elf\0"u8);
        RemoveAutostartAt("/data/OnionHEN/payloads/NoUpdate.elf\0"u8);
    }

    private static void RemoveAutostartAt(ReadOnlySpan<byte> elfPath)
    {
        byte* marker = stackalloc byte[elfPath.Length + 12];
        int nameLength = elfPath.Length - 1; 
        for (int i = 0; i < nameLength; i++)
            marker[i] = elfPath[i];

        ReadOnlySpan<byte> suffix = ".auto_start\0"u8;
        for (int i = 0; i < suffix.Length; i++)
            marker[nameLength + i] = suffix[i];

        if (PayloadFileSystem.unlink(marker) == 0)
            PayloadCrt.Klog("sp:cpb:autostart-off\n\0"u8);
    }

    
    
    
    
    private static bool WriteSite(int pid, ulong site, byte* patch, int length,
        PayloadKernelIo io, ulong cr3, ulong dmapBase)
    {
        if (PayloadProcessMemory.Write(pid, patch, (nint)site, (nuint)length) == 0)
        {
            PayloadCrt.Klog("sp:cpb:w-mdbg\n\0"u8);
            return true;
        }

        if (cr3 != 0 && dmapBase != 0 &&
            KernelPaging.PhysCopyin(io, cr3, dmapBase, site, patch, length))
        {
            PayloadCrt.Klog("sp:cpb:w-phys\n\0"u8);
            return true;
        }

        return false;
    }

    
    
    
    private static bool FindTextByProbe(int pid, out ulong start, out ulong size)
    {
        start = 0;
        size = 0;

        const ulong probeFrom = 0x10000000;
        const ulong probeTo = 0x80000000;
        const ulong step = 0x10000;

        byte[] immB = { 0x3C, 0x00, 0xA4, 0x80 };
        byte[] maskB = { 0xFF, 0xFF, 0xFF, 0xFF };
        byte* probe = stackalloc byte[16];
        byte* head = stackalloc byte[6];

        for (ulong block = probeFrom; block + step <= probeTo; block += step)
        {
            if (PayloadProcessMemory.Read(pid, (nint)block, probe, 16) != 0)
                continue;

            nint match = PayloadProcessMemory.PatternScan(
                pid, (nint)block, (nuint)step, immB, maskB);
            if (match == 0 || (ulong)match < block + 2)
                continue;

            ulong site = (ulong)match - 2;
            if (PayloadProcessMemory.Read(pid, (nint)site, head, 6) != 0)
                continue;
            if (head[0] != 0x41 || head[1] != 0xBF)
                continue;

            start = site > 0x100000 ? site - 0x100000 : probeFrom;
            size = 0x200000;
            KlogHex("sp:cpb:probe-b "u8, site);
            return true;
        }

        return false;
    }

    private static void KlogHex(ReadOnlySpan<byte> prefix, ulong value)
    {
        Span<byte> buf = stackalloc byte[64];
        int n = 0;
        for (int i = 0; i < prefix.Length && n < 40; i++)
            buf[n++] = prefix[i];

        buf[n++] = (byte)'0';
        buf[n++] = (byte)'x';
        for (int shift = 60; shift >= 0; shift -= 4)
        {
            int nib = (int)((value >> shift) & 0xF);
            buf[n++] = (byte)(nib < 10 ? '0' + nib : 'a' + nib - 10);
        }
        buf[n++] = (byte)'\n';
        buf[n++] = 0;

        PayloadCrt.Klog(buf[..n]);
    }
}
