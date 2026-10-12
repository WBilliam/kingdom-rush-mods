ReverseGold = ReverseGold or {}
local M = ReverseGold

M.VERSION = "1.0.1"
M.SETTING_KEY = "reverse_gold_spending"
M._enabled = M._enabled == true
M._hooked_stores = M._hooked_stores or setmetatable({}, {__mode = "k"})
M._balances = M._balances or setmetatable({}, {__mode = "k"})
M._storage = M._storage or nil

local function log(message)
    local line = os.date("!%Y-%m-%dT%H:%M:%SZ") .. " " .. tostring(message) .. "\n"
    if love and love.filesystem and love.filesystem.append then
        pcall(love.filesystem.append, "reverse_gold.log", line)
    end
    if print then
        print("[Reverse Gold] " .. tostring(message))
    end
end

function M.set_enabled(value)
    local next_value = value == true
    if M._enabled ~= next_value then
        M._enabled = next_value
        log("toggle=" .. tostring(next_value))
    end
    return M._enabled
end

function M.is_enabled()
    return M._enabled == true
end

function M.apply_cost(balance, cost)
    if type(balance) ~= "number" or type(cost) ~= "number" then
        error("apply_cost expects numeric balance and cost")
    end
    if cost <= 0 then
        return balance
    end
    if M.is_enabled() then
        return balance + cost
    end
    return balance - cost
end

function M.normalize_settings(settings)
    if type(settings) ~= "table" then
        settings = {}
    end
    settings[M.SETTING_KEY] = settings[M.SETTING_KEY] == true
    return settings
end

local function chained_index(previous, target, key)
    if type(previous) == "function" then
        return previous(target, key)
    elseif type(previous) == "table" then
        return previous[key]
    end
    return nil
end

local function chained_newindex(previous, target, key, value)
    if type(previous) == "function" then
        return previous(target, key, value)
    elseif type(previous) == "table" then
        previous[key] = value
        return
    end
    rawset(target, key, value)
end

local PURCHASE_SOURCES = {
    ["@./all/systems.lua"] = true,
    ["@./all-desktop/game_gui.lua"] = true,
    ["@./all/scripts.lua"] = true
}

local function is_purchase_context()
    if rawget(_G, "DEBUG") == true or not debug or type(debug.getinfo) ~= "function" then
        return false
    end
    local info = debug.getinfo(3, "S")
    local source = info and info.source and string.gsub(info.source, "\\", "/") or ""
    return PURCHASE_SOURCES[source] == true
end

function M.hook_store(store)
    if rawget(_G, "DEBUG") == true then
        return false
    end
    if type(store) ~= "table" or M._hooked_stores[store] then
        return false
    end
    local initial = rawget(store, "player_gold")
    if type(initial) ~= "number" then
        return false
    end
    local prior = getmetatable(store) or {}
    local replacement = {}
    for key, value in pairs(prior) do
        replacement[key] = value
    end
    local previous_index = prior.__index
    local previous_newindex = prior.__newindex
    M._balances[store] = initial
    rawset(store, "player_gold", nil)
    replacement.__index = function(target, key)
        if key == "player_gold" then
            return M._balances[target]
        end
        return chained_index(previous_index, target, key)
    end
    replacement.__newindex = function(target, key, value)
        if key == "player_gold" then
            local current = M._balances[target]
            if type(current) == "number" and type(value) == "number" and value < current
                and M.is_enabled() and is_purchase_context() then
                M._balances[target] = current + (current - value)
            else
                M._balances[target] = value
            end
            return
        end
        return chained_newindex(previous_newindex, target, key, value)
    end
    setmetatable(store, replacement)
    M._hooked_stores[store] = true
    log("player_gold interceptor attached")
    return true
end

local function raw_settings(storage)
    if type(storage) ~= "table" or type(storage.load_lua) ~= "function" then
        return nil
    end
    local ok, value = pcall(storage.load_lua, storage, storage.SETTINGS_FILE or "settings.lua")
    if not ok then
        ok, value = pcall(storage.load_lua, storage.SETTINGS_FILE or "settings.lua")
    end
    return ok and type(value) == "table" and value or nil
end

function M.patch_storage(storage)
    if type(storage) ~= "table" or storage._reverse_gold_patched then
        return false
    end
    if type(storage.load_settings) ~= "function" or type(storage.save_settings) ~= "function" then
        return false
    end
    storage._reverse_gold_patched = true
    M._storage = storage
    local old_load = storage.load_settings
    local old_save = storage.save_settings
    storage.load_settings = function(self, ...)
        local settings = old_load(self, ...)
        local persisted = raw_settings(self)
        if persisted and persisted[M.SETTING_KEY] == true then
            settings[M.SETTING_KEY] = true
        end
        M.normalize_settings(settings)
        M.set_enabled(settings[M.SETTING_KEY])
        return settings
    end
    storage.save_settings = function(self, settings, should_sync, ...)
        M.normalize_settings(settings)
        M.set_enabled(settings[M.SETTING_KEY])
        local result = old_save(self, settings, should_sync, ...)
        if type(self.write_lua) == "function" then
            local persisted = raw_settings(self) or {}
            persisted[M.SETTING_KEY] = M.is_enabled()
            local ok, err = pcall(self.write_lua, self, self.SETTINGS_FILE or "settings.lua", persisted, should_sync)
            if not ok then
                log("settings persistence failed: " .. tostring(err))
            end
        end
        return result
    end
    local current = raw_settings(storage)
    M.set_enabled(current and current[M.SETTING_KEY] == true)
    log("settings hook attached")
    return true
end

function M.save_toggle()
    local storage = M._storage
    if not storage then
        log("toggle changed before storage became available")
        return
    end
    local requested_value = M.is_enabled()
    local settings = storage:load_settings()
    settings[M.SETTING_KEY] = requested_value
    storage:save_settings(settings)
end

local function make_checkbox(width, height, text)
    if type(KView) ~= "table" then
        return nil, "KView API unavailable"
    end
    local function vector(x, y)
        return {x = x, y = y}
    end
    local box = KView:new(vector(width, height))
    local label_class = type(GGOptionsLabel) == "table" and GGOptionsLabel or KLabel
    if type(label_class) ~= "table" then
        return nil, "label API unavailable"
    end
    local label = label_class:new(vector(width, height))
    box.text = text
    box.checked = false
    box.propagate_on_click = false
    label.propagate_on_click = true
    label.text_align = "left"
    label.vertical_align = "middle"
    label.fit_lines = 1
    label.text_offset = vector(8, 0)
    box:add_child(label)
    box._reverse_gold_label = label
    function box:on_change(value)
        return
    end
    function box:set_check(value)
        self.checked = value == true
        self._reverse_gold_label.text = (self.checked and "[X] " or "[ ] ") .. self.text
        self:on_change(self.checked)
    end
    function box:on_click()
        self:set_check(not self.checked)
    end
    box:set_check(false)
    return box
end

local function add_checkbox(view, parent, y)
    if not view or view._reverse_gold_checkbox then
        return
    end
    local checkbox, checkbox_error = make_checkbox(300, 42, "Reverse Gold Spending")
    if not checkbox then
        log("checkbox creation failed: " .. tostring(checkbox_error))
        return
    end
    checkbox.id = "reverse_gold_spending"
    checkbox.pos = {x = math.max(0, (parent.size.x - 300) / 2), y = y}
    checkbox.on_change = function(_, value)
        if view._reverse_gold_refreshing then
            return
        end
        M.set_enabled(value == true)
        M.save_toggle()
    end
    parent:add_child(checkbox)
    view._reverse_gold_checkbox = checkbox
end

local function patch_view_class(class_table, parent_selector, y)
    if type(class_table) ~= "table" or class_table._reverse_gold_patched then
        return false
    end
    if type(class_table.initialize) ~= "function" then
        return false
    end
    class_table._reverse_gold_patched = true
    local old_initialize = class_table.initialize
    local old_show = class_table.show
    class_table.initialize = function(self, ...)
        old_initialize(self, ...)
        local parent = parent_selector(self)
        if parent and parent.add_child and parent.size then
            add_checkbox(self, parent, y)
        end
    end
    if type(old_show) == "function" then
        class_table.show = function(self, ...)
            local result = old_show(self, ...)
            if self._reverse_gold_checkbox then
                self._reverse_gold_refreshing = true
                self._reverse_gold_checkbox:set_check(M.is_enabled())
                self._reverse_gold_refreshing = false
            end
            return result
        end
    end
    return true
end

function M.patch_ui()
    local changed = false
    if patch_view_class(_G.OptionsView, function(view) return view.back or view end, 395) then
        log("campaign options checkbox hook attached")
        changed = true
    end
    if patch_view_class(_G.PauseView, function(view) return view end, 272) then
        log("pause options checkbox hook attached")
        changed = true
    end
    return changed
end

function M.scan_game()
    M.patch_ui()
    if type(_G.storage) == "table" then
        M.patch_storage(_G.storage)
    end
    local game = rawget(_G, "game")
    if type(game) == "table" then
        if type(game.store) == "table" then
            M.hook_store(game.store)
        end
        if type(game.simulation) == "table" and type(game.simulation.store) == "table" then
            M.hook_store(game.simulation.store)
        end
    end
end

function M.bootstrap()
    if M._bootstrapped then
        return
    end
    M._bootstrapped = true
    local original_require = require
    require = function(name)
        local loaded = original_require(name)
        if type(loaded) == "table" then
            M.patch_storage(loaded)
        end
        M.patch_ui()
        return loaded
    end
    log("bootstrap version=" .. M.VERSION)
end

function M.after_main()
    if love and type(love.update) == "function" and not M._update_wrapped then
        M._update_wrapped = true
        local original_update = love.update
        love.update = function(...)
            local result = original_update(...)
            M.scan_game()
            return result
        end
    end
    M.scan_game()
    log("initialized")
end

return M

