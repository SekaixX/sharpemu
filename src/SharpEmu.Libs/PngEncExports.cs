// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;

namespace SharpEmu.Libs.PngEnc;

/// <summary>
/// Initialization surface for the system PNG encoder. The encoder owns no host
/// resource at create time: its caller-provided guest block is the live handle.
/// </summary>
public static class PngEncExports
{
    private const int PngEncErrorInvalidAddress = unchecked((int)0x80690101);
    private const int PngEncErrorInvalidSize = unchecked((int)0x80690102);
    private const int PngEncErrorInvalidParameter = unchecked((int)0x80690103);
    private const uint CreateParameterSize = 0x10;
    private const uint ContextSize = 0x10;
    private const uint MaximumImageWidth = 1_000_000;
    private const uint MaximumFilterCount = 4;
    private const ulong ContextMagic = 0x5348_4152_504E_4745; // "SHARPNGE"

    private readonly record struct CreateParameters(
        uint Size,
        uint Attribute,
        uint MaximumImageWidth,
        uint MaximumFilterCount);

    [SysAbiExport(
        Nid = "9030RnBDoh4",
        ExportName = "scePngEncQueryMemorySize",
        Target = Generation.Gen5,
        LibraryName = "libScePngEnc")]
    public static int PngEncQueryMemorySize(CpuContext ctx)
    {
        var validationResult = ReadAndValidateCreateParameters(
            ctx,
            ctx[CpuRegister.Rdi],
            out _);
        if (validationResult != 0)
        {
            return ctx.SetReturn(validationResult);
        }

        ctx[CpuRegister.Rax] = ContextSize;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "7aGTPfrqT9s",
        ExportName = "scePngEncCreate",
        Target = Generation.Gen5,
        LibraryName = "libScePngEnc")]
    public static int PngEncCreate(CpuContext ctx)
    {
        var parameterAddress = ctx[CpuRegister.Rdi];
        var memoryAddress = ctx[CpuRegister.Rsi];
        var memorySize = unchecked((uint)ctx[CpuRegister.Rdx]);
        var handleAddress = ctx[CpuRegister.Rcx];

        var validationResult = ReadAndValidateCreateParameters(
            ctx,
            parameterAddress,
            out var parameters);
        if (validationResult != 0)
        {
            return ctx.SetReturn(validationResult);
        }

        if (memoryAddress == 0 || handleAddress == 0)
        {
            return ctx.SetReturn(PngEncErrorInvalidAddress);
        }

        if (memorySize < ContextSize)
        {
            return ctx.SetReturn(PngEncErrorInvalidSize);
        }

        Span<byte> context = stackalloc byte[(int)ContextSize];
        context.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(context, ContextMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(context[8..], parameters.MaximumImageWidth);
        BinaryPrimitives.WriteUInt32LittleEndian(context[12..], parameters.MaximumFilterCount);

        Span<byte> handle = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(handle, memoryAddress);
        if (!ctx.Memory.TryWrite(memoryAddress, context) ||
            !ctx.Memory.TryWrite(handleAddress, handle))
        {
            return ctx.SetReturn(PngEncErrorInvalidAddress);
        }

        return ctx.SetReturn(0);
    }

    private static int ReadAndValidateCreateParameters(
        CpuContext ctx,
        ulong address,
        out CreateParameters parameters)
    {
        parameters = default;
        if (address == 0)
        {
            return PngEncErrorInvalidAddress;
        }

        Span<byte> payload = stackalloc byte[(int)CreateParameterSize];
        if (!ctx.Memory.TryRead(address, payload))
        {
            return PngEncErrorInvalidAddress;
        }

        parameters = new CreateParameters(
            BinaryPrimitives.ReadUInt32LittleEndian(payload),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]));

        if (parameters.Size != CreateParameterSize ||
            parameters.Attribute != 0 ||
            parameters.MaximumFilterCount > MaximumFilterCount)
        {
            return PngEncErrorInvalidParameter;
        }

        return parameters.MaximumImageWidth is 0 or > MaximumImageWidth
            ? PngEncErrorInvalidSize
            : 0;
    }
}
