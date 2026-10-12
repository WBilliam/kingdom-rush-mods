MultiHero = MultiHero or {}
local M = MultiHero

M.VERSION = "0.1.3"
M.MAX_PARTY = 4
M.HOTKEYS = { ["3"] = 1, ["4"] = 2, ["5"] = 3, ["6"] = 4 }
M.FORMATION = {
    {x = 0, y = 0}, {x = 32, y = 12}, {x = -32, y = 12}, {x = 0, y = 28}
}

local function unlocked(status, name)
    local record = type(status) == "table" and status[name] or nil
    if type(record) ~= "table" or record.unlocked == false then
        return false
    end
    local required = M._hero_unlock_levels and M._hero_unlock_levels[name]
    if type(required) == "number" and type(M._current_unlock_level_count) == "number"
        and required > M._current_unlock_level_count then
        return false
    end
    return true
end

local function mirror_selected(heroes)
    heroes.selected = heroes.party[1]
end

function M.normalize_heroes(heroes)
    if type(heroes) ~= "table" then
        return {}
    end
    if type(heroes.status) ~= "table" then
        heroes.status = {}
    end
    local source = heroes.party
    if type(source) ~= "table" then
        source = {}
        if type(heroes.selected) == "string" then
            source[1] = heroes.selected
        end
    end
    local party, seen = {}, {}
    for _, name in ipairs(source) do
        if #party >= M.MAX_PARTY then
            break
        end
        if type(name) == "string" and name ~= "" and not seen[name]
            and unlocked(heroes.status, name) then
            party[#party + 1] = name
            seen[name] = true
        end
    end
    heroes.party = party
    mirror_selected(heroes)
    return party
end

function M.capture_hero_unlocks(map_data)
    local hero_data = type(map_data) == "table" and map_data.hero_data or nil
    if type(hero_data) ~= "table" then
        return false
    end
    M._hero_unlock_levels = {}
    for _, hero in ipairs(hero_data) do
        if type(hero) == "table" and type(hero.name) == "string" then
            M._hero_unlock_levels[hero.name] = tonumber(hero.available_level) or 0
        end
    end
    return true
end

function M.normalize_slot(slot)
    if type(slot) ~= "table" then
        return {}
    end
    slot.heroes = type(slot.heroes) == "table" and slot.heroes or {status = {}}
    M._current_unlock_level_count = type(slot.levels) == "table" and #slot.levels or nil
    return M.normalize_heroes(slot.heroes)
end

function M.toggle_hero(heroes, name)
    local party = M.normalize_heroes(heroes)
    if type(name) ~= "string" or not unlocked(heroes.status, name) then
        return false, "unavailable"
    end
    for index, current in ipairs(party) do
        if current == name then
            table.remove(party, index)
            mirror_selected(heroes)
            return true, "removed"
        end
    end
    if #party >= M.MAX_PARTY then
        return false, "full"
    end
    party[#party + 1] = name
    mirror_selected(heroes)
    return true, "added"
end

function M.move_hero(heroes, index, delta)
    local party = M.normalize_heroes(heroes)
    if type(index) ~= "number" or type(delta) ~= "number" then
        return false
    end
    local destination = index + delta
    if index < 1 or index > #party or destination < 1 or destination > #party then
        return false
    end
    party[index], party[destination] = party[destination], party[index]
    mirror_selected(heroes)
    return true
end

function M.sync_party_xp(store, slot, ephemeral)
    if ephemeral == true or type(store) ~= "table" or type(slot) ~= "table"
        or type(slot.heroes) ~= "table" then
        return false
    end
    M.normalize_heroes(slot.heroes)
    local entities = store._multi_hero_entities
    if type(entities) ~= "table" then
        return false
    end
    local changed = false
    for _, entity in pairs(entities) do
        local name = type(entity) == "table" and (entity.template_name or entity.hero_id) or nil
        local xp = type(entity) == "table" and entity.hero_xp or nil
        if type(entity) == "table" and type(entity.hero) == "table" then
            xp = entity.hero.xp
        end
        local status = name and slot.heroes.status[name] or nil
        if type(status) == "table" and type(xp) == "number"
            and (type(status.xp) ~= "number" or xp > status.xp) then
            status.xp = xp
            changed = true
        end
    end
    return changed
end

function M.hotkey_slot(key)
    return M.HOTKEYS[key]
end

local function entity_available(entity)
    if type(entity) ~= "table" then
        return false
    end
    if entity.health and (entity.health.dead == true or (type(entity.health.hp) == "number" and entity.health.hp <= 0)) then
        return false
    end
    if entity.hero and (entity.hero.dead == true or entity.hero.respawning == true) then
        return false
    end
    if entity.ui and entity.ui.can_click == false then
        return false
    end
    return entity.pending_removal ~= true and entity.removed ~= true
end

function M.select_hotkey(gui, store, key)
    local slot = M.hotkey_slot(key)
    local entities = type(store) == "table" and store._multi_hero_entities or nil
    local entity = slot and type(entities) == "table" and entities[slot] or nil
    if not entity_available(entity) then
        return false
    end
    if type(gui) == "table" and type(gui.select_entity) == "function" then
        gui:select_entity(entity)
        return true
    end
    if entity.ui then
        entity.ui.clicked = true
        return true
    end
    return false
end

local function log(message)
    local line = os.date("!%Y-%m-%dT%H:%M:%SZ") .. " " .. tostring(message) .. "\n"
    if love and love.filesystem and love.filesystem.append then
        pcall(love.filesystem.append, "multi_hero.log", line)
    end
    if print then
        print("[Four Hero] " .. tostring(message))
    end
end

local function ephemeral_xp()
    local ok, settings = pcall(require, "game_settings")
    return ok and type(settings) == "table" and settings.hero_xp_ephemeral == true
end

function M.patch_storage(storage)
    if type(storage) ~= "table" or storage._multi_hero_patched
        or type(storage.load_slot) ~= "function" or type(storage.save_slot) ~= "function" then
        return false
    end
    storage._multi_hero_patched = true
    M._storage = storage
    local old_load, old_save = storage.load_slot, storage.save_slot
    storage.load_slot = function(self, index, ...)
        local slot = old_load(self, index, ...)
        if type(slot) == "table" then
            local slot_id = index or self.active_slot_idx
            if M._active_slot_id ~= nil and slot_id ~= nil and slot_id ~= M._active_slot_id then
                M._active_store = nil
            end
            M._active_slot_id = slot_id
            M.normalize_slot(slot)
            M._active_slot = slot
        end
        return slot
    end
    storage.save_slot = function(self, slot, index, ...)
        if type(slot) == "table" then
            M.normalize_slot(slot)
            local target_id = index or self.active_slot_idx
            local store_id = type(M._active_store) == "table" and M._active_store._multi_hero_slot_id or nil
            if target_id == nil or store_id == nil or target_id == store_id then
                M.sync_party_xp(M._active_store, slot, ephemeral_xp())
            end
            M._active_slot = slot
        end
        return old_save(self, slot, index, ...)
    end
    log("storage hook attached")
    return true
end

local function apply_formation(entity, first, index)
    local offset = M.FORMATION[index]
    if index == 1 or type(entity) ~= "table" or type(first) ~= "table"
        or type(entity.pos) ~= "table" or type(first.pos) ~= "table" or not offset then
        return
    end
    entity.pos.x = first.pos.x + offset.x
    entity.pos.y = first.pos.y + offset.y
end

function M.patch_level_utils(level_utils)
    if type(level_utils) ~= "table" or level_utils._multi_hero_patched
        or type(level_utils.insert_hero) ~= "function" then
        return false
    end
    level_utils._multi_hero_patched = true
    local old_insert = level_utils.insert_hero
    level_utils.insert_hero = function(store, ...)
        if type(store) ~= "table" then
            return old_insert(store, ...)
        end
        if store._multi_hero_spawned then
            return store.main_hero
        end
        local slot = M._active_slot
        local heroes = type(slot) == "table" and slot.heroes or nil
        local party = type(heroes) == "table" and M.normalize_heroes(heroes) or {}
        store._multi_hero_spawned = true
        store._multi_hero_entities = {}
        if #party == 0 then
            store.main_hero = nil
            return nil
        end
        local previous_name, previous_status = store.selected_hero, store.selected_hero_status
        local first
        for index, name in ipairs(party) do
            store.selected_hero = name
            store.selected_hero_status = heroes.status[name]
            store.main_hero = nil
            local level = type(store.level) == "table" and store.level or nil
            local previous_locked_hero = level and level.locked_hero
            if level then
                level.locked_hero = false
            end
            M._inserting_party_slot = index
            local ok, returned = pcall(old_insert, store, ...)
            M._inserting_party_slot = nil
            if level then
                level.locked_hero = previous_locked_hero
            end
            if not ok then
                error(returned, 0)
            end
            local entity = returned or store.main_hero
            if type(entity) == "table" then
                entity._multi_hero_slot = index
                store._multi_hero_entities[index] = entity
                first = first or entity
                apply_formation(entity, first, index)
            else
                log("hero insertion produced no entity for slot " .. tostring(index))
            end
        end
        store.main_hero = first
        store.selected_hero = party[1] or previous_name
        store.selected_hero_status = party[1] and heroes.status[party[1]] or previous_status
        M._active_store = store
        store._multi_hero_slot_id = M._active_slot_id
        return first
    end
    M._level_utils = level_utils
    log("level_utils hook attached")
    return true
end

local function add_badge(view, text, id)
    if type(view) ~= "table" or view[id] then
        return
    end
    local label_class = rawget(_G, "GGLabel") or rawget(_G, "KLabel")
    if type(label_class) ~= "table" or type(label_class.new) ~= "function"
        or type(view.add_child) ~= "function" then
        return
    end
    local ok, label = pcall(label_class.new, label_class, {x = 28, y = 24})
    if not ok or type(label) ~= "table" then
        return
    end
    label.id = id
    label.text = tostring(text)
    label.font_size = 18
    label.text_align = "center"
    label.vertical_align = "middle"
    label.pos = {x = 3, y = 3}
    label.colors = label.colors or {}
    label.colors.background = {25, 20, 10, 220}
    label.colors.text = {255, 235, 120, 255}
    view:add_child(label)
    view[id] = label
end

local function arrange_hud(gui, entity)
    local slot = entity and entity._multi_hero_slot
    local view = slot and gui.heroes and gui.heroes[#gui.heroes] or nil
    if not slot or type(view) ~= "table" then
        return
    end
    gui._multi_hero_hud_origin = gui._multi_hero_hud_origin or {
        x = view.pos and view.pos.x or 0, y = view.pos and view.pos.y or 0
    }
    view._multi_hero_slot = slot
    local hero_views = {}
    for _, portrait in ipairs(gui.heroes) do
        hero_views[portrait] = true
        if portrait._multi_hero_slot then
            local portrait_slot = portrait._multi_hero_slot
            local column = (portrait_slot - 1) % 2
            local row = math.floor((portrait_slot - 1) / 2)
            portrait.pos = portrait.pos or {x = 0, y = 0}
            portrait.pos.x = gui._multi_hero_hud_origin.x + column * 84
            portrait.pos.y = gui._multi_hero_hud_origin.y - row * 82
            portrait.scale = portrait.scale or {x = 1, y = 1}
            portrait.scale.x, portrait.scale.y = 0.78, 0.78
            if type(portrait.set_style) == "function" then
                portrait:set_style()
            end
            add_badge(portrait, portrait_slot + 2, "_multi_hero_badge")
        end
    end
    local herobar = gui.hud_bottom and gui.hud_bottom.herobar
    if type(herobar) == "table" and type(herobar.children) == "table" then
        for _, child in ipairs(herobar.children) do
            if not hero_views[child] then
                child.hidden = true
            end
        end
    end
    if slot == 1 and gui.hud_bottom then
        add_badge(gui.hud_bottom, "3 / 4 / 5 / 6", "_multi_hero_key_hint")
        local hint = gui.hud_bottom._multi_hero_key_hint
        if hint then
            hint.size = {x = 150, y = 24}
            hint.pos = {x = gui._multi_hero_hud_origin.x, y = gui._multi_hero_hud_origin.y - 145}
        end
    end
end

function M.patch_game_gui(game_gui)
    if type(game_gui) ~= "table" or game_gui._multi_hero_patched then
        return false
    end
    game_gui._multi_hero_patched = true
    if type(game_gui.keypressed) == "function" then
        local old_keypressed = game_gui.keypressed
        game_gui.keypressed = function(self, key, ...)
            if key == "space" or key == rawget(_G, "KEYPRESS_SPACE") then
                return true
            end
            if M.hotkey_slot(key) then
                local store = self.game and (self.game.store or (self.game.simulation and self.game.simulation.store))
                if M.select_hotkey(self, store, key) then
                    return true
                end
                local view = self.heroes and self.heroes[M.hotkey_slot(key)]
                if view and type(view.on_click) == "function" then
                    view:on_click(1, 0, 0)
                end
                return true
            end
            return old_keypressed(self, key, ...)
        end
    end
    if type(game_gui.add_hero) == "function" then
        local old_add = game_gui.add_hero
        game_gui.add_hero = function(self, entity, ...)
            if type(entity) == "table" and entity._multi_hero_slot == nil
                and M._inserting_party_slot ~= nil then
                entity._multi_hero_slot = M._inserting_party_slot
            end
            local result = old_add(self, entity, ...)
            arrange_hud(self, entity)
            return result
        end
    end
    log("game GUI hook attached")
    return true
end

local function append_unique(list, value)
    for _, current in ipairs(list) do
        if current == value then
            return false
        end
    end
    list[#list + 1] = value
    return true
end

function M.patch_director(director)
    if type(director) ~= "table" or director._multi_hero_patched
        or type(director.queue_load_item_named) ~= "function" then
        return false
    end
    director._multi_hero_patched = true
    local old_queue = director.queue_load_item_named
    director.queue_load_item_named = function(self, name, ...)
        local result = old_queue(self, name, ...)
        if name ~= "game" then
            return result
        end
        local heroes = type(M._active_slot) == "table" and M._active_slot.heroes or nil
        local party = type(heroes) == "table" and M.normalize_heroes(heroes) or {}
        local queued = self.queued_item
        local game_item = rawget(_G, "game")
        if type(queued) == "table" and queued.item_name == "game" then
            game_item = queued
        elseif type(queued) == "table" and type(queued.game_item) == "table" then
            game_item = queued.game_item
        end
        local level = type(game_item) == "table" and type(game_item.store) == "table"
            and game_item.store.level or nil
        if #party < 2 or type(level) ~= "table" then
            return result
        end
        level.required_textures = type(level.required_textures) == "table"
            and level.required_textures or {}
        level.required_sounds = type(level.required_sounds) == "table"
            and level.required_sounds or {}
        local textures, sounds = {}, {}
        for index = 2, #party do
            local sound = party[index]
            local texture = "go_" .. sound
            if append_unique(level.required_textures, texture) then
                textures[#textures + 1] = texture
            end
            if append_unique(level.required_sounds, sound) then
                sounds[#sounds + 1] = sound
            end
        end
        if #textures > 0 and type(self.load_texture_groups) == "function" then
            self:load_texture_groups(textures, self.params and self.params.texture_size,
                game_item.ref_res, true, "game")
        end
        if #sounds > 0 and type(self.load_sound_groups) == "function" then
            self:load_sound_groups(sounds)
        end
        return result
    end
    log("director resource hook attached")
    return true
end

local function room_slot(room)
    if type(room) == "table" and type(room._multi_hero_slot) == "table" then
        return room._multi_hero_slot
    end
    if M._storage and type(M._storage.load_slot) == "function" then
        local ok, slot = pcall(M._storage.load_slot, M._storage)
        if ok then
            return slot
        end
    end
    return M._active_slot
end

local function room_reject(room)
    if type(room.multi_hero_reject) == "function" then
        room:multi_hero_reject()
        return
    end
    local sound = rawget(_G, "S")
    if type(sound) == "table" and type(sound.queue) == "function" then
        pcall(sound.queue, sound, "GUIButtonUnavailable")
    end
end

local function save_room_slot(room, slot)
    if M._storage and type(M._storage.save_slot) == "function" then
        M._storage:save_slot(slot)
    end
    M.refresh_hero_room(room, slot)
end

function M.hero_room_action(room, action, index)
    local slot = room_slot(room)
    local heroes = type(slot) == "table" and slot.heroes or nil
    local party = type(heroes) == "table" and M.normalize_heroes(heroes) or nil
    if not party or type(index) ~= "number" then
        return false
    end
    local changed = false
    if action == "remove" and party[index] then
        table.remove(party, index)
        mirror_selected(heroes)
        changed = true
    elseif action == "left" then
        changed = M.move_hero(heroes, index, -1)
    elseif action == "right" then
        changed = M.move_hero(heroes, index, 1)
    end
    if changed then
        save_room_slot(room, slot)
    end
    return changed
end

local function make_room_label(parent, text, x, width)
    local label_class = rawget(_G, "GGLabel") or rawget(_G, "KLabel")
    if type(label_class) ~= "table" or type(label_class.new) ~= "function" then
        return nil
    end
    local ok, label = pcall(label_class.new, label_class, {x = width, y = 30})
    if not ok or type(label) ~= "table" then
        return nil
    end
    label.text, label.pos = text, {x = x, y = 0}
    label.font_size, label.fit_lines = 15, 1
    label.text_align, label.vertical_align = "center", "middle"
    label.colors = label.colors or {}
    label.colors.text = {255, 235, 160, 255}
    label.propagate_on_click = false
    parent:add_child(label)
    return label
end

local function bind_room_action(control, room, action, index)
    control.on_click = function()
        M.hero_room_action(room, action, index)
    end
end

local function ensure_room_strip(room)
    if room._multi_hero_strip then
        return room._multi_hero_strip
    end
    local view_class = rawget(_G, "KView")
    local parent = room.back or room
    if type(view_class) ~= "table" or type(view_class.new) ~= "function"
        or type(parent.add_child) ~= "function" then
        return nil
    end
    local ok, strip = pcall(view_class.new, view_class, {x = 620, y = 36})
    if not ok or type(strip) ~= "table" then
        return nil
    end
    local parent_height = parent.size and parent.size.y or 760
    strip.id = "multi_hero_party_strip"
    strip.pos = {x = 70, y = parent_height - 78}
    strip.slots = {}
    for index = 1, M.MAX_PARTY do
        local x = (index - 1) * 150
        local item = make_room_label(strip, tostring(index + 2) .. "  --", x + 20, 90)
        local left = make_room_label(strip, "<", x, 20)
        local right = make_room_label(strip, ">", x + 110, 20)
        local remove = make_room_label(strip, "x", x + 130, 20)
        if item and left and right and remove then
            bind_room_action(left, room, "left", index)
            bind_room_action(right, room, "right", index)
            bind_room_action(remove, room, "remove", index)
            strip.slots[index] = {item = item, left = left, right = right, remove = remove}
        end
    end
    parent:add_child(strip)
    room._multi_hero_strip = strip
    return strip
end

function M.refresh_hero_room(room, slot)
    if type(room) ~= "table" or type(slot) ~= "table" or type(slot.heroes) ~= "table" then
        return
    end
    local party = M.normalize_heroes(slot.heroes)
    local thumbs = type(room.get_child_by_id) == "function" and room:get_child_by_id("hero_thumbs") or nil
    for name in pairs(slot.heroes.status) do
        local thumb = thumbs and type(thumbs.get_child_by_id) == "function" and thumbs:get_child_by_id(name) or nil
        if thumb and thumb._multi_hero_party_badge then
            thumb._multi_hero_party_badge.hidden = true
        end
        if thumb and type(thumb.on_click) == "function" and not thumb._multi_hero_click_patched then
            thumb._multi_hero_click_patched = true
            local old_click = thumb.on_click
            local hero_name = name
            thumb.on_click = function(this, ...)
                local result = old_click(this, ...)
                local locked = type(room.get_child_by_id) == "function"
                    and room:get_child_by_id("hero_room_sel_locked") or nil
                if not locked or locked.hidden ~= false then
                    room:select_hero(hero_name)
                end
                return result
            end
        end
    end
    if room.hero_shown and type(room.get_child_by_id) == "function" then
        local is_selected = false
        for _, name in ipairs(party) do
            is_selected = is_selected or name == room.hero_shown
        end
        local select_button = room:get_child_by_id("hero_room_sel_select")
        local deselect_button = room:get_child_by_id("hero_room_sel_deselect")
        if select_button then select_button.hidden = is_selected end
        if deselect_button then deselect_button.hidden = not is_selected end
    end
    for index, name in ipairs(party) do
        local thumb = thumbs and type(thumbs.get_child_by_id) == "function" and thumbs:get_child_by_id(name) or nil
        if thumb then
            add_badge(thumb, index + 2, "_multi_hero_party_badge")
            if thumb._multi_hero_party_badge then
                thumb._multi_hero_party_badge.hidden = false
                thumb._multi_hero_party_badge.text = tostring(index + 2)
            end
        end
    end
    local strip = ensure_room_strip(room)
    if strip and strip.slots then
        for index = 1, M.MAX_PARTY do
            local controls = strip.slots[index]
            if controls then
                local name = party[index]
                controls.item.text = tostring(index + 2) .. "  " .. (name or "--")
                controls.left.hidden = not name or index == 1
                controls.right.hidden = not name or index == #party
                controls.remove.hidden = not name
            end
        end
    end
    room._multi_hero_party = party
end

function M.patch_hero_room_class(class_table)
    if type(class_table) ~= "table" or class_table._multi_hero_patched
        or type(class_table.select_hero) ~= "function" then
        return false
    end
    class_table._multi_hero_patched = true
    local old_select = class_table.select_hero
    class_table.select_hero = function(self, name, silent, ...)
        local slot = room_slot(self)
        if type(slot) ~= "table" then
            return old_select(self, name, silent, ...)
        end
        slot.heroes = type(slot.heroes) == "table" and slot.heroes or {status = {}}
        local party = M.normalize_heroes(slot.heroes)
        if type(name) ~= "string" or name == "" then
            M.refresh_hero_room(self, slot)
            return false
        end
        local already = false
        for _, current in ipairs(party) do
            already = already or current == name
        end
        if not already and #party >= M.MAX_PARTY then
            room_reject(self)
            return false
        end
        old_select(self, name, silent, ...)
        if silent == true then
            M.refresh_hero_room(self, slot)
            return true
        end
        local changed = M.toggle_hero(slot.heroes, name)
        if changed and M._storage and type(M._storage.save_slot) == "function" then
            M._storage:save_slot(slot)
        end
        M.refresh_hero_room(self, slot)
        return changed
    end
    local old_deselect = class_table.deselect_hero
    if type(old_deselect) == "function" then
        class_table.deselect_hero = function(self, name, ...)
            local result = old_deselect(self, name, ...)
            local slot = room_slot(self)
            local party = type(slot) == "table" and type(slot.heroes) == "table"
                and M.normalize_heroes(slot.heroes) or {}
            for index, current in ipairs(party) do
                if current == name then
                    table.remove(party, index)
                    mirror_selected(slot.heroes)
                    save_room_slot(self, slot)
                    return true
                end
            end
            return result or false
        end
    end
    local old_show = class_table.show
    if type(old_show) == "function" then
        class_table.show = function(self, ...)
            local result = old_show(self, ...)
            M.refresh_hero_room(self, room_slot(self))
            return result
        end
    end
    log("Hero Room hook attached")
    return true
end

function M.scan_game()
    M.patch_hero_room_class(rawget(_G, "HeroRoomViewKR1"))
    if type(rawget(_G, "storage")) == "table" then
        M.patch_storage(rawget(_G, "storage"))
    end
    local game = rawget(_G, "game")
    local store = type(game) == "table" and (game.store or (game.simulation and game.simulation.store)) or nil
    if type(store) == "table" then
        M._active_store = store
        local locations = type(store.level) == "table" and store.level.locations or nil
        local exits = type(locations) == "table" and locations.exits or nil
        if not store._multi_hero_spawned and M._level_utils
            and type(exits) == "table" and exits[1] ~= nil then
            M._level_utils.insert_hero(store)
        end
    end
end

function M.bootstrap()
    if M._bootstrapped then
        return
    end
    M._bootstrapped = true
    local old_require = require
    require = function(name)
        local loaded = old_require(name)
        if name == "storage" then
            M.patch_storage(loaded)
        elseif name == "level_utils" then
            M.patch_level_utils(loaded)
        elseif name == "game_gui" then
            M.patch_game_gui(loaded)
        elseif name == "director" then
            M.patch_director(loaded)
        elseif name == "data.map_data" then
            M.capture_hero_unlocks(loaded)
        end
        return loaded
    end
    log("bootstrap version=" .. M.VERSION)
end

function M.after_main()
    M.patch_hero_room_class(rawget(_G, "HeroRoomViewKR1"))
    M.patch_director(rawget(_G, "director"))
    if love and type(love.update) == "function" and not M._update_wrapped then
        M._update_wrapped = true
        local old_update = love.update
        love.update = function(...)
            local result = old_update(...)
            M.scan_game()
            return result
        end
    end
    M.scan_game()
    log("initialized")
end

return M

