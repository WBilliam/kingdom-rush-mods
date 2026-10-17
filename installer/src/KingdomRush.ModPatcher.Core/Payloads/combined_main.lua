local reverse_chunk, reverse_error = love.filesystem.load("reverse_gold.lua")
if not reverse_chunk then error("Reverse Gold: " .. tostring(reverse_error)) end
local reverse_ok, reverse_runtime = pcall(reverse_chunk)
if not reverse_ok then error("Reverse Gold: " .. tostring(reverse_runtime)) end
reverse_runtime.bootstrap()

local multi_chunk, multi_error = love.filesystem.load("multi_hero.lua")
if not multi_chunk then error("Four-Hero: " .. tostring(multi_error)) end
local multi_ok, multi_runtime = pcall(multi_chunk)
if not multi_ok then error("Four-Hero: " .. tostring(multi_runtime)) end
multi_runtime.bootstrap()

local original, original_error = love.filesystem.load("_modpack_main_orig.lua")
if not original then error("Original main: " .. tostring(original_error)) end
local loaded, game_error = pcall(original)
if not loaded then error("Original main: " .. tostring(game_error)) end

reverse_runtime.after_main()
multi_runtime.after_main()
