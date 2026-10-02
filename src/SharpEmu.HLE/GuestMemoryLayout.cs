// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.HLE;

public enum GuestVirtualAddressPlacement
{
    Lazy,
    Canonical,
}

public static class GuestMemoryLayout
{
    public const string VirtualAddressPlacementVariable = "SHARPEMU_GUEST_VA_PLACEMENT";
    public const string PreReservedPrimaryUserWindowVariable = "SHARPEMU_PRE_RESERVED_PRIMARY_USER_WINDOW";
    public const string TrustedPreReservedPrimaryUserWindowVariable = "SHARPEMU_TRUSTED_PRE_RESERVED_PRIMARY_USER_WINDOW";
    public const string PreReservedPrimaryUserWindowMarker = "0000001000000000:0000008000000000";
    public const ulong GuestAddressStart = 0x0000_0000_0004_0000;
    public const ulong GuestUserAddressStart = 0x0000_0010_0000_0000;
    public const ulong GuestPrimaryUserAddressLimit = 0x0000_0090_0000_0000;
    public const ulong GuestPrimaryUserAddressSize = GuestPrimaryUserAddressLimit - GuestUserAddressStart;
    public const ulong GuestAddressLimit = 0x0000_00FC_0000_0000;
    public const ulong DirectBytes = 16384UL * 1024 * 1024;
    public const ulong FlexibleBytes = 448UL * 1024 * 1024;
    public const ulong FlexibleOffset = DirectBytes;
    public const ulong BackingBytes = DirectBytes + FlexibleBytes;
    public const ulong GuestPage = 0x4000;

    public static GuestVirtualAddressPlacement VirtualAddressPlacement =>
        ResolveVirtualAddressPlacement(Environment.GetEnvironmentVariable(VirtualAddressPlacementVariable));

    internal static GuestVirtualAddressPlacement ResolveVirtualAddressPlacement(string? value) =>
        // The PS5 user address window is part of the ABI: GPU-visible guest
        // addresses and a number of engine allocators assume the canonical
        // 0x1000000000..0x9000000000 range.  Keep the experimental lazy
        // reservation available only when explicitly requested.
        string.Equals(value?.Trim(), "lazy", StringComparison.OrdinalIgnoreCase)
            ? GuestVirtualAddressPlacement.Lazy
            : GuestVirtualAddressPlacement.Canonical;
}
