# Colony Command — Full Feature Parity Plan

## Guiding Principle: Test Before Deploy

Every change to the mod (C#) or dashboard (JS) goes through:

1. **Pre-deploy validation script** → `bash ~/.hermes/skills/gaming/rimworld-dashboard/scripts/test-suite.sh`
   - JS syntax check + duplicate let detection + DOM ID consistency + HTML structure
   - Full API endpoint smoke test (all endpoints return success)
   - Pawn switching test (two different pawns return different names)
   - Speed control test (POST /api/colony/time with {action:...})
   - Chronicle narrative rendering test
   - **All checks must pass before deploy**

2. **Feature-specific curl test** → if you're adding/editing an endpoint, test it with curl from WSL before the user touches the dashboard

3. **Deploy + user refresh** → HTML changes only (no restart needed), or DLL change (restart needed)

4. **User validation** → user checks the feature on their phone

---

## PHASE 1: Core Colony Control (Easy — ~1 session)

### 1.1 Work Priorities per Pawn

**C# work:** Add POST `/api/pawns/priorities` handler
- Body: `{pawn: "id", priorities: {shooting: 1, construction: 2, ...}}`
- Sets `pawn.workSettings.SetPriority(def, priority)` for each work type
- Returns full priority list

**Dashboard work:** Add "Work" tab to pawn detail showing 4×4 priority grid
- 16 work types × priority 1-4 buttons
- Visual: colored buttons (1=green, 2=yellow, 3=orange, 4=red)
- Submit button to save

**Testing:**
- `curl -X POST -H 'Content-Type: application/json' -d '{"pawn":"50740","priorities":{"shooting":1,"mining":3}}' http://...` → check response
- Verify in-game the pawn's priorities actually changed
- Dashboard: click priority button, submit, verify grid updates

### 1.2 Forbid/Unforbid Items

**C# work:** Add POST `/api/colony/forbid` handler
- Body: `{thing: "thing_label", forbid: true/false}`
- Sets `thing.IsForbidden() = forbid`

**Dashboard work:** Add forbid toggle button next to each resource in the resource list

**Testing:**
- Resource list shows items with count
- Toggle forbid on an item → curl check or verify in-game the item has red X
- Refresh dashboard, see toggle state persist

### 1.3 Pawn Equipment Management

**C# work:** Add POST `/api/pawns/equip` handler
- Body: `{pawn: "id", item: "item_name", slot: "weapon|apparel|inventory"}`
- Uses existing `EquipGear` or builds new endpoint
- Also need GET `/api/pawns/{id}/inventory` to list all items the pawn is carrying

**Dashboard work:** Add "Inventory" tab to pawn detail showing all carried items with equip/unequip buttons

**Testing:**
- List pawn's inventory → verify items shown
- Equip a weapon → verify pawn's equipment changes
- Remove/drop an item → verify item gone from inventory

### 1.4 Rename Pawns

**C# work:** Add POST `/api/pawns/rename` handler
- Body: `{pawn: "id", first: "New", nick: "Name", last: "Last"}`
- Sets `pawn.Name = new NameTriple(first, nick, last)`

**Dashboard work:** Add rename button in pawn detail header that expands to three name fields + save

**Testing:**
- Rename a pawn via curl → verify name changes in pawn list response
- Refresh dashboard → new name shows in pawn list and detail

---

## PHASE 2: Advanced Management (Medium — 1-2 sessions)

### 2.1 Bill Management at Production Benches

**C# work:**
- Add GET `/api/bills` → list all work tables with their production bills
- Add POST `/api/bills/add` → add a bill to a work table
- Add POST `/api/bills/remove` → remove a bill by ID
- Each bill has: recipe, count, quality, hit points, ingredient radius, suspend toggle

**Dashboard work:** New "Bills" tab or section
- Shows all work benches (tailor, smithy, cook stove, etc.)
- Each bench shows its bills with controls: count adjust, suspend, delete
- "Add Bill" button with recipe picker

**Testing:**
- List bills → verify benches shown
- Add bill: "Make components × 5" at smithy → verify in-game
- Remove bill → verify gone
- Suspend/resume → verify in-game state changes

### 2.2 Direct Pawn Commands

**C# work:** Add POST `/api/pawns/command` handler
- Body: `{pawn: "id", command: "prioritize_haul|prioritize_build|tend|rescue|arrest", target?: "thing_id"}`
- This is the hardest C# work — requires creating a `Job` object and queuing it on the pawn's `jobQueue`
- `pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Haul, thing))` or similar

**Dashboard work:** Action buttons in pawn detail
- "Prioritize: Haul" "Prioritize: Build" "Tend" "Rescue" "Arrest"
- Target picker (list of nearby things/pawns to target)

**Testing:**
- Command pawn to haul → verify pawn moves to haul something
- Command pawn to tend → verify pawn starts tending injured pawn
- Command pawn to rescue → verify pawn carries wounded pawn to bed

### 2.3 Prisoner Management

**C# work:**
- Add GET `/api/prisoners` → list prisoners with health, mood, recruit progress
- Add POST `/api/prisoners/action` → `{prisoner: "id", action: "recruit|release|execute|reduce_resistance"}`
- Prisoners are pawns with `IsPrisonerOfMyFaction = true`

**Dashboard work:** Prisoner section in pawns tab or new tab
- List prisoners with their stats
- Action buttons: Recruit, Release, Execute
- Recruit progress bar

**Testing:**
- After a raid with survivors captured → verify prisoners appear in list
- Release a prisoner → verify gone from list, verify in-game
- Recruit → verify they join colony (may need multiple attempts)

### 2.4 Bionic/Implant Management

**C# work:** Add POST `/api/pawns/surgery` handler
- Body: `{pawn: "id", body_part: "heart", implant: "bionic_heart", action: "install|remove"}`
- Uses `HediffMaker.MakeHediff(recipe.addsHediff, pawn, part)`
- Or wraps `RecipeDef` surgery recipes

**Dashboard work:** "Bionics" tab in pawn detail
- Shows current implants/body parts
- Available upgrades dropdown
- Install/remove buttons

**Testing:**
- Install bionic arm on pawn → verify pawn stats show it
- Check in-game pawn's health tab → bionic arm present
- Remove → verify gone

---

## PHASE 3: Full Colony Control (Hard — 2-3 sessions)

### 3.1 Blueprint Placement

**C# work:** 
This is the hardest feature. RimWorld's building system uses `Designator` classes which require map interaction.
- Add POST `/api/build/blueprint` → `{thing: "wall|door|workbench|etc", x: 100, z: 150, rotation: 0}`
- Requires instantiating a `ThingDef` and creating a `Blueprint` at map position
- `GenSpawn.Spawn(ThingDefOf.Wall blueprint, position, map)`
- Need map coordinate validation (prevent building off-map)

**Dashboard work:** 
This is tricky on mobile — no live map view. Options:
- Text input: enter coordinates (x, z) + thing type
- Quick buttons: "Wall at cursor" (uses last known position)
- Grid overlay showing empty/taken cells near colony center

**Testing:**
- Place wall blueprint → verify blueprint appears in-game at coordinates
- Place workbench → verify blueprint appears
- Deconstruct blueprint → verify it disappears

### 3.2 Stockpile Zone Management

**C# work:**
- Add GET `/api/zones` → list stockpile zones with positions and storage settings
- Add POST `/api/zones/create` → `{name, cell1: {x,z}, cell2: {x,z}, store_whatever: true/false}` 
- Add POST `/api/zones/storage` → `{zone_id, settings: {hitPoints: 51-100, quality: normal+, ...}}`

**Dashboard work:** Zone list in new "Zones" tab
- Show all stockpiles with their settings
- Create new zone (enter top-left + bottom-right coordinates)
- Toggle storage filters (allow rotten, allow fresh, HP range, quality range)
- Expand/shrink zone

**Testing:**
- List zones → verify at least one exists (colony has stockpiles)
- Create a new 5×5 stockpile → verify in-game on the ground
- Change filter (don't allow tainted apparel) → verify items don't get stored there

### 3.3 Caravan / Transport System

**C# work:** 
- Add POST `/api/caravan/form` → `{pawns: ["id1","id2"], items: [{thing: "steel", count: 200}], destination: "tile|settlement"}`
- Requires `CaravanFormingHelper.AllSendablePawns(map)` to list available pawns
- `CaravanFormingHelper.FormAndSendCaravan(...)` 
- Complex: needs destination tile calculation, pawn/item capacity checks

**Dashboard work:** New "Caravan" tab
- List all pawns available for caravan
- Select pawns to send (checkboxes)
- Add supplies (food, medicine, weapons)
- Set destination (friendly settlement, nearby tile)
- Send button

**Testing:**
- Form a 2-pawn caravan with 100 food → verify caravan leaves map in-game
- Check world map → caravan visible traveling
- Complete journey → verify pawns return

### 3.4 Faction Diplomacy & Trading

**C# work:**
- Add GET `/api/factions` → list factions with goodwill, relationship status
- Add POST `/api/factions/goodwill` → `{faction: "Pirate", change: 20}` to adjust relations
- Add GET `/api/trade` → list available trade goods from caravans/settlements
- Add POST `/api/trade/execute` → `{faction: "Settlement", sell: [{thing:"yayo", count:50}], buy: [{thing:"plasteel", count:100}]}`

**Dashboard work:** New "Factions" tab
- List all factions with goodwill meters
- Adjust goodwill buttons (+20 gift, -30 attack, etc.)
- Trade panel: items to sell/buy with counts

**Testing:**
- List factions → verify all known factions shown
- Gift silver to faction → verify goodwill increases
- Buy items from settlement → verify items arrive in colony stockpile

---

## Infrastructure Improvements Along the Way

### After Each Feature
1. Add the new API endpoint to the test suite script
2. Add a dashboard card test: verify the JS function loads, container exists, error state renders
3. Update AGENTS.md and README.md with the new endpoint

### Consolidate Polling
After Phase 1, replace the 7 independent setInterval timers with a single tick at 5s:
```javascript
async function tick() {
  await loadTime();
  await loadOverview();
  await loadResources();
  await loadPawns();
  await loadResearch();
  await loadGoals();
  await loadEventHistory();
  await loadChronicle();
  await pollEvents();
  await pollChat();
}
setInterval(tick, 5000);
```
Add a `/api/batch` call instead of 8 individual requests.

### Error State Standard
Every dashboard card follows this pattern:
```javascript
async function loadFeature() {
  const c = document.getElementById('feature-container');
  c.innerHTML = '<div class="loading">Loading...</div>';
  try {
    const data = await api('/api/feature');
    if (!data || (Array.isArray(data) && !data.length)) {
      c.innerHTML = '<div class="empty-state">No data yet</div>';
      return;
    }
    c.innerHTML = renderFeature(data);
  } catch(e) {
    c.innerHTML = `<div class="error-state">⚠️ Failed to load — <button onclick="loadFeature()">retry</button></div>`;
    dbg('feature', e.message);
  }
}
```

---

## Quick Reference: What Goes Where

| Change | File | Restart? |
|--------|------|----------|
| New C# API endpoint | `~/rimworld-mcp/RimworldMcp/Source/ApiHandlers/<NewHandler>.cs` + route in `HttpServer.cs` | Game restart |
| Fix C# bug | Edit existing `.cs` | Game restart |
| HTML structure change | `dashboard.html` | Refresh browser |
| CSS change | `dashboard.html` `<style>` block | Refresh browser |
| JS function change | `dashboard.html` `<script>` block | Refresh browser |
| New dashboard tab | `dashboard.html` (HTML + nav + JS) | Refresh browser |
| Test suite update | `scripts/test-suite.sh` | N/A |
| Project docs | `AGENTS.md`, `README.md` | N/A |
