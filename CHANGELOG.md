- Revert change to 406 On to the Drydocks -alydev
- Add missing amaljaa mount def -alydev
- BossMod presets — We now only  
- -turn off our own presets ("Questionable" and "Questionable – Quest Battles"). - KAge
- Removed an old, outdated BossMod helper — It was silently changing two BossMod settings and only changing one back,
  leaving the other stuck. - Kage
- Always switch off BossMod's old AI mode (/vbmai off) — Whenever we set one of our presets or end combat, we make sure
  the deprecated "vbmai" auto-pilot is turned off. This prevents the old AI and the new preset from running at the same
  time and stepping on each other. - Kage
- YesAlready now gets turned back on properly — Before, if you closed Questionable while it had paused YesAlready,
  YesAlready would stay paused forever. Now it gets re-enabled on shutdown. - Kage
- Stopping mid-cutscene during a solo duty cleans up properly — If you hit Stop during a duty cutscene, BossMod's AI and
  our preset both shut down now (previously the AI could be left running). - Kage