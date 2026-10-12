from pathlib import Path
import unittest
import zipfile

from tests.lua_harness import Lua51, lua_path


ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "src" / "multi_hero.lua"
LUA_DLL = Path(r"D:\Steam\steamapps\common\Kingdom Rush\lua51.dll")


@unittest.skipUnless(LUA_DLL.exists(), "target game's Lua 5.1 runtime is unavailable")
class MultiHeroLuaTests(unittest.TestCase):
    def run_lua(self, body: str) -> list[str]:
        source = f"local M = assert(loadfile('{lua_path(MODULE)}'))()\n{body}"
        with Lua51(LUA_DLL) as lua:
            return lua.run(source)

    def test_single_selection_migrates_and_mirrors_selected(self):
        got = self.run_lua("""
local heroes = {selected='hero_archer', status={hero_archer={xp=20}}}
local party = M.normalize_heroes(heroes)
return table.concat(party, ','), heroes.selected
""")
        self.assertEqual(["hero_archer", "hero_archer"], got)

    def test_empty_party_is_valid_and_clears_compatibility_selection(self):
        got = self.run_lua("""
local heroes = {selected=false, party={}, status={hero_archer={xp=20}}}
local party = M.normalize_heroes(heroes)
return #party, tostring(heroes.selected)
""")
        self.assertEqual(["0", "nil"], got)

    def test_invalid_locked_duplicate_and_fifth_entries_are_filtered_stably(self):
        got = self.run_lua("""
local heroes = {
  party={'a', 'b', 'a', 42, 'locked', 'unknown', 'c', 'd', 'e'},
  status={a={}, b={}, locked={unlocked=false}, c={}, d={}, e={}}
}
local party = M.normalize_heroes(heroes)
return table.concat(party, ','), heroes.selected
""")
        self.assertEqual(["a,b,c,d", "a"], got)

    def test_map_unlock_levels_filter_heroes_not_yet_available(self):
        got = self.run_lua("""
M.capture_hero_unlocks({hero_data={
  {name='a', available_level=1}, {name='late', available_level=8}
}})
local slot={levels={{},{},{}}, heroes={party={'a','late'}, status={a={},late={}}}}
local party=M.normalize_slot(slot)
return table.concat(party, ','), slot.heroes.selected
""")
        self.assertEqual(["a", "a"], got)

    def test_toggle_rejects_fifth_and_supports_remove(self):
        got = self.run_lua("""
local heroes = {party={'a','b','c','d'}, status={a={},b={},c={},d={},e={}}}
M.normalize_heroes(heroes)
local changed, reason = M.toggle_hero(heroes, 'e')
local removed = M.toggle_hero(heroes, 'b')
return tostring(changed), reason, tostring(removed), table.concat(heroes.party, ','), heroes.selected
""")
        self.assertEqual(["false", "full", "true", "a,c,d", "a"], got)

    def test_reorder_updates_order_and_selected_mirror(self):
        got = self.run_lua("""
local heroes = {party={'a','b','c'}, status={a={},b={},c={}}}
M.normalize_heroes(heroes)
local moved = M.move_hero(heroes, 2, -1)
return tostring(moved), table.concat(heroes.party, ','), heroes.selected
""")
        self.assertEqual(["true", "b,a,c", "b"], got)

    def test_xp_sync_writes_max_for_every_party_hero(self):
        got = self.run_lua("""
local slot = {heroes={party={'a','b','c','d'}, status={a={xp=10},b={xp=99},c={},d={xp=1}}}}
local store = {_multi_hero_entities={
  {hero_id='a', hero_xp=25}, {hero_id='b', hero_xp=40},
  {hero_id='c', hero_xp=8}, {hero_id='d', hero_xp=2}
}}
local changed = M.sync_party_xp(store, slot, false)
return tostring(changed), slot.heroes.status.a.xp, slot.heroes.status.b.xp,
       slot.heroes.status.c.xp, slot.heroes.status.d.xp
""")
        self.assertEqual(["true", "25", "99", "8", "2"], got)

    def test_ephemeral_xp_does_not_write(self):
        got = self.run_lua("""
local slot = {heroes={party={'a'}, status={a={xp=10}}}}
local store = {_multi_hero_entities={{hero_id='a', hero_xp=50}}}
local changed = M.sync_party_xp(store, slot, true)
return tostring(changed), slot.heroes.status.a.xp
""")
        self.assertEqual(["false", "10"], got)

    def test_hotkeys_map_to_slots_and_unavailable_entities_are_safe(self):
        got = self.run_lua("""
local selected
local gui = {select_entity=function(self, entity) selected=entity.hero_id end}
local store = {_multi_hero_entities={
  {hero_id='a'}, {hero_id='b', health={dead=true}}, nil, {hero_id='d', ui={can_click=false}}
}}
local a = M.select_hotkey(gui, store, '3')
local b = M.select_hotkey(gui, store, '4')
local x = M.select_hotkey(gui, store, '2')
return tostring(a), selected, tostring(b), tostring(x),
       tostring(M.hotkey_slot('3')), tostring(M.hotkey_slot('6'))
""")
        self.assertEqual(["true", "a", "false", "false", "1", "4"], got)

    def test_storage_hook_migrates_on_load_and_syncs_xp_before_save(self):
        got = self.run_lua("""
local saved
local slot = {heroes={selected='a', status={a={xp=2}, b={xp=3}}}}
local storage = {
  load_slot=function(self) return slot end,
  save_slot=function(self, value) saved=value return 'saved' end
}
M.patch_storage(storage)
local loaded = storage:load_slot()
M._active_store = {_multi_hero_entities={
  {template_name='a', hero={xp=7}}, {template_name='b', hero={xp=9}}
}}
loaded.heroes.party[2] = 'b'
storage:save_slot(loaded)
return table.concat(saved.heroes.party, ','), saved.heroes.selected,
       saved.heroes.status.a.xp, saved.heroes.status.b.xp
""")
        self.assertEqual(["a,b", "a", "7", "9"], got)

    def test_switching_save_slots_clears_stale_gameplay_xp_source(self):
        got = self.run_lua("""
local slots={
 [1]={heroes={party={'a'},status={a={xp=1}}}},
 [2]={heroes={party={'a'},status={a={xp=2}}}}
}
local storage={active_slot_idx=1}
function storage:load_slot(idx) return slots[idx or self.active_slot_idx] end
function storage:save_slot(slot, idx) slots[idx or self.active_slot_idx]=slot end
M.patch_storage(storage)
storage:load_slot()
M._active_store={_multi_hero_slot_id=1,_multi_hero_entities={{template_name='a',hero={xp=99}}}}
storage:save_slot(slots[1])
storage.active_slot_idx=2
local second=storage:load_slot()
storage:save_slot(second)
return slots[1].heroes.status.a.xp, slots[2].heroes.status.a.xp,
       tostring(M._active_store), tostring(M._active_slot_id)
""")
        self.assertEqual(["99", "2", "nil", "2"], got)

    def test_level_utils_hook_spawns_party_once_and_keeps_first_as_main(self):
        got = self.run_lua("""
local calls = 0
local lu = {insert_hero=function(store)
  calls=calls+1
  store.main_hero={template_name=store.selected_hero, pos={x=100,y=50}, hero={xp=0}}
  return store.main_hero
end}
M._active_slot = {heroes={party={'a','b','c','d'}, status={a={},b={},c={},d={}}}}
M.patch_level_utils(lu)
local store = {selected_hero='a', selected_hero_status={}, level={}}
lu.insert_hero(store)
lu.insert_hero(store)
return calls, store.main_hero.template_name, #store._multi_hero_entities,
  store._multi_hero_entities[2]._multi_hero_slot,
  store._multi_hero_entities[2].pos.x, store._multi_hero_entities[4].pos.y
""")
        self.assertEqual(["4", "a", "4", "2", "132", "78"], got)

    def test_locked_mode_spawns_party_and_restores_native_restriction(self):
        got = self.run_lua("""
local calls, saw_unlocked = 0, true
local lu = {insert_hero=function(store)
  calls = calls + 1
  saw_unlocked = saw_unlocked and store.level.locked_hero == false
  if store.level.locked_hero then return nil end
  store.main_hero={template_name=store.selected_hero, pos={x=100,y=50}, hero={xp=0}}
  return store.main_hero
end}
M._active_slot={heroes={party={'a','b','c','d'}, status={a={},b={},c={},d={}}}}
M.patch_level_utils(lu)
local store={selected_hero='a', selected_hero_status={}, level={locked_hero=true}}
local first=lu.insert_hero(store)
return calls, tostring(saw_unlocked), tostring(store.level.locked_hero),
  tostring(first and first.template_name), #store._multi_hero_entities
""")
        self.assertEqual(["4", "true", "true", "a", "4"], got)

    def test_scan_game_waits_for_level_exit_locations_before_fallback_spawn(self):
        got = self.run_lua("""
local calls = 0
local lu = {insert_hero=function(store)
  calls = calls + 1
  assert(store.level.locations.exits, 'native insert requires level exits')
  store.main_hero={template_name=store.selected_hero, pos={x=100,y=50}, hero={xp=0}}
  return store.main_hero
end}
M._active_slot = {heroes={party={'a'}, status={a={}}}}
M.patch_level_utils(lu)
M._level_utils = lu
game = {store={selected_hero='a', selected_hero_status={}, level={locations={}}}}
M.scan_game()
local before = calls
game.store.level.locations.exits = {{x=100, y=50}}
M.scan_game()
return before, calls, game.store.main_hero.template_name
""")
        self.assertEqual(["0", "1", "a"], got)

    def test_game_gui_hook_reserves_fixed_keys_and_removes_space_shortcut(self):
        got = self.run_lua("""
local old_calls, selected = 0, ''
local gui_module = {
  keypressed=function(self, key) old_calls=old_calls+1 return 'old:'..key end,
  select_entity=function(self, entity) selected=entity.template_name end
}
M.patch_game_gui(gui_module)
local gui = setmetatable({game={store={_multi_hero_entities={{template_name='a'}}}}}, {__index=gui_module})
local three = gui:keypressed('3')
local space = gui:keypressed('space')
local other = gui:keypressed('x')
return tostring(three), selected, tostring(space), other, old_calls
""")
        self.assertEqual(["true", "a", "true", "old:x", "1"], got)

    def test_spawn_slot_is_available_when_native_gui_adds_each_portrait(self):
        got = self.run_lua("""
GGLabel = nil
local gui_module = {add_hero=function(self, entity)
  self.heroes[#self.heroes + 1] = {pos={x=10, y=20}}
end}
M.patch_game_gui(gui_module)
local gui = setmetatable({heroes={}, hud_bottom={}}, {__index=gui_module})
local lu = {insert_hero=function(store)
  local entity={template_name=store.selected_hero, pos={x=100,y=50}, hero={xp=0}}
  store.main_hero=entity
  gui:add_hero(entity)
  return entity
end}
M._active_slot={heroes={party={'a','b','c','d'}, status={a={},b={},c={},d={}}}}
M.patch_level_utils(lu)
lu.insert_hero({selected_hero='a', selected_hero_status={}, level={}})
return #gui.heroes,
  gui.heroes[1].pos.x, gui.heroes[1].pos.y,
  gui.heroes[2].pos.x, gui.heroes[2].pos.y,
  gui.heroes[3].pos.x, gui.heroes[3].pos.y,
  gui.heroes[4].pos.x, gui.heroes[4].pos.y
""")
        self.assertEqual(["4", "10", "20", "94", "20", "10", "-62", "94", "-62"], got)

    def test_four_portrait_hud_uses_clean_standalone_grid(self):
        got = self.run_lua("""
GGLabel = nil
local function portrait()
  local view={pos={x=15,y=0}, style='default'}
  function view:set_style(style) self.style=style or 'default' end
  return view
end
local gui_module={add_hero=function(self, entity)
  local view=portrait()
  self.heroes[#self.heroes + 1]=view
  local children=self.hud_bottom.herobar.children
  children[#children + 1]=view
  if #self.heroes > 1 then
    self.heroes[1]:set_style('left')
    view.pos={x=99,y=0}
    view:set_style('right')
    children[#children + 1]={kind='separator',hidden=false}
  end
end}
M.patch_game_gui(gui_module)
local gui=setmetatable({heroes={},hud_bottom={herobar={children={}}}},{__index=gui_module})
for slot=1,4 do
  M._inserting_party_slot=slot
  gui:add_hero({_multi_hero_slot=slot})
end
M._inserting_party_slot=nil
local hidden=0
for _,child in ipairs(gui.hud_bottom.herobar.children) do
  if child.kind=='separator' and child.hidden then hidden=hidden+1 end
end
return gui.heroes[1].pos.x,gui.heroes[1].pos.y,
  gui.heroes[2].pos.x,gui.heroes[2].pos.y,
  gui.heroes[3].pos.x,gui.heroes[3].pos.y,
  gui.heroes[4].pos.x,gui.heroes[4].pos.y,
  table.concat({gui.heroes[1].style,gui.heroes[2].style,
    gui.heroes[3].style,gui.heroes[4].style},','),hidden
""")
        self.assertEqual([
            "15", "0", "99", "0", "15", "-82", "99", "-82",
            "default,default,default,default", "3",
        ], got)

    def test_director_queues_and_tracks_resources_for_extra_party_heroes(self):
        got = self.run_lua("""
local texture_calls, sound_calls = {}, {}
local director = {
  params={texture_size='ipad'},
  queue_load_item_named=function(self, name)
    game={ref_res=768, store={level={required_textures={'stage'}, required_sounds={'music'}}}}
    return 'queued:' .. name
  end,
  load_texture_groups=function(self, groups)
    texture_calls[#texture_calls + 1]=table.concat(groups, ',')
  end,
  load_sound_groups=function(self, groups)
    sound_calls[#sound_calls + 1]=table.concat(groups, ',')
  end
}
M._active_slot={heroes={party={'hero_gerald','hero_alleria','hero_ignus','hero_bolin'},
  status={hero_gerald={},hero_alleria={},hero_ignus={},hero_bolin={}}}}
local patched=M.patch_director(director)
local result=director:queue_load_item_named('game')
return tostring(patched), result,
  texture_calls[1], sound_calls[1],
  table.concat(game.store.level.required_textures, ','),
  table.concat(game.store.level.required_sounds, ',')
""")
        self.assertEqual([
            "true", "queued:game",
            "go_hero_alleria,go_hero_ignus,go_hero_bolin",
            "hero_alleria,hero_ignus,hero_bolin",
            "stage,go_hero_alleria,go_hero_ignus,go_hero_bolin",
            "music,hero_alleria,hero_ignus,hero_bolin",
        ], got)

    def test_reverse_gold_and_multi_hero_require_wrappers_chain_together(self):
        exe = Path(r"D:\Steam\steamapps\common\Kingdom Rush\Kingdom Rush.exe")
        with zipfile.ZipFile(exe) as archive:
            reverse_gold = archive.read("reverse_gold.lua").decode("utf-8")
        def lua_literal(value: str) -> str:
            return "'" + value.replace("\\", "\\\\").replace("'", "\\'").replace("\r", "\\r").replace("\n", "\\n") + "'"
        source = f"""
love={{filesystem={{append=function() end}}}}
local reverse=assert(loadstring({lua_literal(reverse_gold)}))()
local multi=assert(loadfile('{lua_path(MODULE)}'))()
reverse.bootstrap()
multi.bootstrap()
package.preload.storage=function()
  return {{
    load_settings=function() return {{}} end,
    save_settings=function() end,
    load_slot=function() return {{heroes={{party={{}},status={{}}}},levels={{}}}} end,
    save_slot=function() end
  }}
end
local storage=require('storage')
return tostring(storage._reverse_gold_patched), tostring(storage._multi_hero_patched)
"""
        with Lua51(LUA_DLL) as lua:
            self.assertEqual(["true", "true"], lua.run(source))

    def test_hero_room_hook_toggles_and_rejects_a_fifth_selection(self):
        got = self.run_lua("""
local saved, rejected = 0, 0
local storage = {save_slot=function(self, slot) saved=saved+1 end}
M._storage = storage
local class = {select_hero=function(self, name) self.vanilla=name end}
M.patch_hero_room_class(class)
local room = setmetatable({
  _multi_hero_slot={heroes={party={'a','b','c','d'}, status={a={},b={},c={},d={},e={}}}},
  multi_hero_reject=function(self) rejected=rejected+1 end
}, {__index=class})
local fifth = room:select_hero('e')
local remove = room:select_hero('b')
return tostring(fifth), tostring(remove),
  table.concat(room._multi_hero_slot.heroes.party, ','), saved, rejected
""")
        self.assertEqual(["false", "true", "a,c,d", "1", "1"], got)

    def test_vanilla_deselect_button_removes_only_the_shown_party_hero(self):
        got = self.run_lua("""
local saves = 0
M._storage = {save_slot=function() saves=saves+1 end}
local class = {
  select_hero=function() end,
  deselect_hero=function(self, name) self.vanilla_deselected=name end
}
M.patch_hero_room_class(class)
local room=setmetatable({_multi_hero_slot={heroes={
  party={'a','b','c'}, status={a={},b={},c={}}
}}}, {__index=class})
local changed=room:deselect_hero('b')
return tostring(changed), room.vanilla_deselected,
  table.concat(room._multi_hero_slot.heroes.party, ','),
  room._multi_hero_slot.heroes.selected, saves
""")
        self.assertEqual(["true", "b", "a,c", "a", "1"], got)

    def test_hero_room_strip_remove_and_reorder_actions_persist(self):
        got = self.run_lua("""
local saves = 0
M._storage = {save_slot=function(self, slot) saves=saves+1 end}
local room = {_multi_hero_slot={heroes={
  party={'a','b','c'}, status={a={},b={},c={}}
}}}
local left = M.hero_room_action(room, 'left', 3)
local right = M.hero_room_action(room, 'right', 1)
local remove = M.hero_room_action(room, 'remove', 2)
return tostring(left), tostring(right), tostring(remove),
  table.concat(room._multi_hero_slot.heroes.party, ','),
  room._multi_hero_slot.heroes.selected, saves
""")
        self.assertEqual(["true", "true", "true", "c,b", "c", "3"], got)

    def test_silent_initial_selection_does_not_toggle_migrated_hero_off(self):
        got = self.run_lua("""
M._storage = {save_slot=function() end}
local class = {select_hero=function(self, name, silent) self.old_called=silent end}
M.patch_hero_room_class(class)
local room = setmetatable({_multi_hero_slot={heroes={
  selected='a', status={a={}}
}}}, {__index=class})
local result = room:select_hero('a', true)
return tostring(result), table.concat(room._multi_hero_slot.heroes.party, ','),
  room._multi_hero_slot.heroes.selected, tostring(room.old_called)
""")
        self.assertEqual(["true", "a", "a", "true"], got)

    def test_empty_party_never_forwards_nil_to_vanilla_selection(self):
        got = self.run_lua("""
local old_calls=0
local class={select_hero=function() old_calls=old_calls+1 end}
M.patch_hero_room_class(class)
local room=setmetatable({_multi_hero_slot={heroes={party={},status={a={}}}}},{__index=class})
local result=room:select_hero(nil,true)
return tostring(result), old_calls, #room._multi_hero_slot.heroes.party,
       tostring(room._multi_hero_slot.heroes.selected)
""")
        self.assertEqual(["false", "0", "0", "nil"], got)

    def test_move_hero_rejects_malformed_indices_without_throwing(self):
        got = self.run_lua("""
local heroes={party={'a','b'},status={a={},b={}}}
local ok1,result1=pcall(M.move_hero,heroes,nil,1)
local ok2,result2=pcall(M.move_hero,heroes,'1',1)
return tostring(ok1),tostring(result1),tostring(ok2),tostring(result2),table.concat(heroes.party,',')
""")
        self.assertEqual(["true", "false", "true", "false", "a,b"], got)

    def test_portrait_click_toggles_unlocked_but_not_locked_hero(self):
        got = self.run_lua("""
local saved = 0
M._storage = {save_slot=function() saved=saved+1 end}
local locked = {hidden=true}
local thumbs = {children={}}
function thumbs:get_child_by_id(name) return self.children[name] end
local function thumb(name, is_locked)
  local item={id=name}
  item.on_click=function() locked.hidden=not is_locked end
  thumbs.children[name]=item
end
thumb('a', false); thumb('b', true)
local room = {
  _multi_hero_slot={heroes={party={}, status={a={},b={}}}}, hero_shown='a',
  get_child_by_id=function(self,id)
    if id=='hero_thumbs' then return thumbs end
    if id=='hero_room_sel_locked' then return locked end
  end,
  select_hero=function(self,name) return M.toggle_hero(self._multi_hero_slot.heroes,name) end
}
M.refresh_hero_room(room, room._multi_hero_slot)
thumbs.children.a:on_click()
thumbs.children.b:on_click()
return table.concat(room._multi_hero_slot.heroes.party, ',')
""")
        self.assertEqual(["a"], got)


if __name__ == "__main__":
    unittest.main()

