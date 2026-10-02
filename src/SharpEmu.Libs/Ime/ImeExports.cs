// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Ime;

public static class ImeExports
{
    private const int ImeErrorInvalidAddress = unchecked((int)0x80BC0031);
    private const int ImeErrorNotOpened = unchecked((int)0x80BC0002);
    private const int ImeErrorInvalidUserId = unchecked((int)0x80BC0010);
    private const int ImeErrorNoResourceId = unchecked((int)0x80BC0023);
    private static int _keyboardOpen;

    public static void ResetRuntimeState() =>
        Interlocked.Exchange(ref _keyboardOpen, 0);

    // Quake (KEX) calls this from its main loop and from the audio bring-up path with
    // an event-handler pointer. No IME session ever exists here, so report success
    // without invoking the handler ("no pending IME events"). This NID was previously
    // misbound as an sceNgs2VoiceControl alias, which fed the game NGS2 errors.
    [SysAbiExport(
        Nid = "-4GCfYdNF1s",
        ExportName = "sceImeUpdate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceIme")]
    public static int ImeUpdate(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "eaFXjfJv3xs",
        ExportName = "sceImeKeyboardOpen",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceIme")]
    public static int ImeKeyboardOpen(CpuContext ctx)
    {
        Interlocked.Exchange(ref _keyboardOpen, 1);
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "PMVehSlfZ94",
        ExportName = "sceImeKeyboardClose",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceIme")]
    public static int ImeKeyboardClose(CpuContext ctx)
    {
        if (Volatile.Read(ref _keyboardOpen) == 0)
        {
            return ctx.SetReturn(ImeErrorNotOpened);
        }

        if (unchecked((int)ctx[CpuRegister.Rdi]) == -1)
        {
            return ctx.SetReturn(ImeErrorInvalidUserId);
        }

        Interlocked.Exchange(ref _keyboardOpen, 0);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "dKadqZFgKKQ",
        ExportName = "sceImeKeyboardGetResourceId",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceIme")]
    public static int ImeKeyboardGetResourceId(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "VkqLPArfFdc",
        ExportName = "sceImeKeyboardGetInfo",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceIme")]
    public static int ImeKeyboardGetInfo(CpuContext ctx)
    {
        if (ctx[CpuRegister.Rsi] == 0)
        {
            return ctx.SetReturn(ImeErrorInvalidAddress);
        }

        return Volatile.Read(ref _keyboardOpen) == 0
            ? ctx.SetReturn(ImeErrorNotOpened)
            : ctx.SetReturn(ImeErrorNoResourceId);
    }
}
