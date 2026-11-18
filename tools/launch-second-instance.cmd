@echo off
rem Starts a SECOND modded copy of the game for local co-op testing.
rem
rem Why this is needed:
rem  - r2modman launches through Steam ("steam.exe -applaunch 2950790 --melonloader.basedir <profile>"), and Steam
rem    refuses to start a game that's already running. Running the exe directly gets around that.
rem  - Two copies can't share one r2modman profile: the second copy died in MelonLoader's startup (empty log),
rem    because the first copy holds the profile's MelonLoader files open. So the second copy uses its own
rem    r2modman profile named "Guest" (same mods). The NestCoop build deploys to it automatically.
rem
rem Start the first copy from r2modman (Default profile) as usual, then run this.
rem If this copy closes right away and Steam opens instead, create a text file named steam_appid.txt containing
rem 2950790 next to the game exe and try again.

set "GAME=B:\SteamLibrary\steamapps\common\Iron Nest Heavy Turret Simulator"
set "PROFILE=%APPDATA%\r2modmanPlus-local\IronNestHeavyTurretSimulator\profiles\Guest"

if not exist "%PROFILE%\MelonLoader" (
  echo Guest profile not set up: "%PROFILE%\MelonLoader" is missing.
  echo In r2modman: create a profile named Guest, install LavaGang-MelonLoader in it, and launch it once.
  pause
  exit /b 1
)

start "" /D "%GAME%" "%GAME%\Iron Nest Heavy Turret Simulator.exe" --melonloader.basedir "%PROFILE%"
