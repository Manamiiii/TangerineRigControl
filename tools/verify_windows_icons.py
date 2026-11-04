# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii
"""Compare icon index zero in Windows PE files with the project's ICO, frame by frame."""
import argparse
import ctypes as ct
from ctypes import wintypes as wt
from pathlib import Path
import struct


def ico_frames(data):
    reserved, kind, count = struct.unpack_from("<HHH", data)
    if (reserved, kind) != (0, 1) or not count:
        raise ValueError("Invalid ICO directory")
    frames = {}
    for index in range(count):
        width, height, _, _, _, _, length, offset = struct.unpack_from("<BBBBHHII", data, 6 + 16 * index)
        frames[width or 256, height or 256] = data[offset:offset + length]
    return frames


def verify(path, expected):
    kernel = ct.WinDLL("kernel32", use_last_error=True)
    ptr = ct.c_void_p
    callback_type = ct.WINFUNCTYPE(wt.BOOL, ptr, ptr, ptr, ct.c_ssize_t)
    def api(name, result, *args):
        fn = getattr(kernel, name)
        fn.restype, fn.argtypes = result, args
        return fn
    load = api("LoadLibraryExW", ptr, wt.LPCWSTR, ptr, wt.DWORD)
    enum = api("EnumResourceNamesW", wt.BOOL, ptr, ptr, callback_type, ct.c_ssize_t)
    find = api("FindResourceW", ptr, ptr, ptr, ptr)
    size = api("SizeofResource", wt.DWORD, ptr, ptr)
    resource = api("LoadResource", ptr, ptr, ptr)
    lock = api("LockResource", ptr, ptr)
    free = api("FreeLibrary", wt.BOOL, ptr)
    module = load(str(path.resolve()), None, 0x22)  # data/image resources only
    if not module:
        raise ct.WinError(ct.get_last_error())
    try:
        def read(name, kind):
            handle = find(module, name, kind)
            if not handle:
                raise ct.WinError(ct.get_last_error())
            address = lock(resource(module, handle))
            if not address:
                raise ct.WinError(ct.get_last_error())
            return ct.string_at(address, size(module, handle))
        groups = []
        @callback_type
        def collect(_module, _kind, name, _context):
            groups.append(read(name, 14))
            return True
        if not enum(module, 14, collect, 0) or not groups:
            raise ValueError("No group icon in " + path.name)
        group = groups[0]
        frames = {}
        for index in range(struct.unpack_from("<H", group, 4)[0]):
            w, h, _, _, _, _, length, icon_id = struct.unpack_from("<BBBBHHIH", group, 6 + 14 * index)
            frames[w or 256, h or 256] = read(icon_id, 3)[:length]
        if frames != expected:
            raise ValueError(f"{path.name}: PE icon frames differ from app.ico: {sorted(frames)}")
        print(f"PASS {path.name}: all {len(frames)} embedded icon frames match app.ico")
    finally:
        free(module)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executables", nargs="+", type=Path)
    args = parser.parse_args()
    expected = ico_frames((Path(__file__).resolve().parents[1] / "src/TangerineRigControl/Assets/app.ico").read_bytes())
    for executable in args.executables:
        verify(executable, expected)
