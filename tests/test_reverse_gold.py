from pathlib import Path
import unittest

from tests.lua_harness import Lua51, lua_path


ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "src" / "reverse_gold.lua"
LUA_DLL = Path(r"D:\Steam\steamapps\common\Kingdom Rush\lua51.dll")


@unittest.skipUnless(LUA_DLL.exists(), "target game's Lua 5.1 runtime is unavailable")
class ReverseGoldLuaTests(unittest.TestCase):
    def run_lua(self, body: str) -> list[str]:
        source = f"local M = assert(loadfile('{lua_path(MODULE)}'))()\n{body}"
        with Lua51(LUA_DLL) as lua:
            return lua.run(source)

    def test_apply_cost_reverses_only_positive_spending_when_enabled(self):
        got = self.run_lua("""
M.set_enabled(false)
local normal = M.apply_cost(100, 25)
M.set_enabled(true)
return normal, M.apply_cost(100, 25), M.apply_cost(100, 0), M.apply_cost(100, -5)
""")
        self.assertEqual(["75", "125", "100", "100"], got)

    def test_settings_are_normalized_and_persisted(self):
        got = self.run_lua("""
local persisted = {reverse_gold_spending=true, unrelated=7}
local writes = 0
local storage = {
  SETTINGS_FILE='settings.lua',
  load_lua=function() return persisted end,
  write_lua=function(self, name, value) persisted=value writes=writes+1 end,
  load_settings=function() return {unrelated=7} end,
  save_settings=function() return 'saved' end
}
M.patch_storage(storage)
local settings = storage:load_settings()
settings.reverse_gold_spending = false
storage:save_settings(settings)
return tostring(M.is_enabled()), tostring(persisted.reverse_gold_spending), persisted.unrelated, writes
""")
        self.assertEqual(["false", "false", "7", "1"], got)
