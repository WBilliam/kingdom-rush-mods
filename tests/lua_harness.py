from __future__ import annotations

import ctypes
from pathlib import Path


LUA_MULTRET = -1


class LuaError(RuntimeError):
    pass


class Lua51:
    """Tiny test-only bridge to the game's own Lua 5.1 runtime."""

    def __init__(self, dll_path: Path):
        self.lib = ctypes.WinDLL(str(dll_path))
        self.lib.luaL_newstate.restype = ctypes.c_void_p
        self.lib.luaL_openlibs.argtypes = [ctypes.c_void_p]
        self.lib.luaL_loadbuffer.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p]
        self.lib.luaL_loadbuffer.restype = ctypes.c_int
        self.lib.lua_pcall.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int]
        self.lib.lua_pcall.restype = ctypes.c_int
        self.lib.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
        self.lib.lua_tolstring.restype = ctypes.c_void_p
        self.lib.lua_gettop.argtypes = [ctypes.c_void_p]
        self.lib.lua_gettop.restype = ctypes.c_int
        self.lib.lua_close.argtypes = [ctypes.c_void_p]
        self.state = self.lib.luaL_newstate()
        if not self.state:
            raise LuaError("luaL_newstate failed")
        self.lib.luaL_openlibs(self.state)

    def close(self) -> None:
        if self.state:
            self.lib.lua_close(self.state)
            self.state = None

    def __enter__(self) -> "Lua51":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def _text(self, index: int) -> str:
        length = ctypes.c_size_t()
        pointer = self.lib.lua_tolstring(self.state, index, ctypes.byref(length))
        if not pointer:
            return ""
        return ctypes.string_at(pointer, length.value).decode("utf-8", "replace")

    def run(self, source: str) -> list[str]:
        payload = source.encode("utf-8")
        status = self.lib.luaL_loadbuffer(self.state, payload, len(payload), b"@multi_hero_test.lua")
        if status:
            raise LuaError(self._text(-1))
        status = self.lib.lua_pcall(self.state, 0, LUA_MULTRET, 0)
        if status:
            raise LuaError(self._text(-1))
        return [self._text(i) for i in range(1, self.lib.lua_gettop(self.state) + 1)]


def lua_path(path: Path) -> str:
    return str(path.resolve()).replace("\\", "/").replace("'", "\\'")

