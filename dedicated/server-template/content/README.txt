content/

What this host reads (drop files in and run: project-renaissance-dedicated check)

  Game Modes/   .bin game variants. A playlist names one as
                { "map": "ForgeWorld", "game": "Game Modes/myvariant.bin" },
                or by a name the game already knows, { "game": "Slayer" }.

  Maps/         saved Forge maps (.mvar) and mod maps (.map + .mapinfo).
                A playlist names one as { "map_variant": "Maps/mymap.mvar" }.

  Mods/         one folder per custom map package, holding its .map and the
                .mapinfo the game needs. Give the folder to a server with
                mods = ["ModFolderName"] in [defaults], and name the map's own
                scenario in a playlist entry ("map": "its scenario name").

Nothing here is distributed with this host: it plays your own copy of Halo: Reach
from Steam, and the maps, game types and mods you add or saved yourself.

Your client's saved maps and game types live in
%LOCALAPPDATA%\ReachRenaissance\fileshare (GameType\ and Map\), and MCC's own
Reach variants in <MCC>\haloreach\hopper_game_variants. A playlist can point
straight at either; "content" lists what this host can see and where.
