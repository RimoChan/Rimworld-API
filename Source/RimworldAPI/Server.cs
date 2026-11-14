using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimworldAPI {
    public class RimworldConverter : JsonConverter {
        public override bool CanRead => false;

        public override bool CanConvert(Type t) {
            return t == typeof(IntVec3) || typeof(Def).IsAssignableFrom(t) || typeof(Hediff).IsAssignableFrom(t) || typeof(Job).IsAssignableFrom(t);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            if (value is IntVec3 v) value = new { x = v.x, y = v.z };
            else if (value is Def d) value = d.defName;
            else if (value is Hediff h) value = new { def = h.def, part = h.Part?.Label, label = h.Label, severity = h.Severity };
            else if (value is Job j) value = j.def;
            serializer.Serialize(writer, value);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            throw new NotImplementedException();
        }
    }

    public class ApiHandler {
        public static readonly object TickSyncLock = new object();

        public Pawn get_pawn(int id) {
            return PawnsFinder.AllMaps_Spawned.FirstOrDefault(p => p.thingIDNumber == id) ?? throw new Exception($"pawn {id} not found.");
        }

        public Thing get_thing(int id) {
            return Find.CurrentMap.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == id) ?? throw new Exception($"thing {id} not found.");
        }

        public Zone get_zone(int id) {
            return Find.CurrentMap.zoneManager.AllZones.FirstOrDefault(z => z.ID == id) ?? throw new Exception("zone not found.");
        }

        public object get_commands(Thing t) {
            var res = new List<object>();
            try {
                foreach (var m in t.GetGizmos().OfType<Command>().Where(m => !(m is Designator)))
                    res.Add(new { key = get_command_key(m), type = m.GetType().FullName, label = m.Label });
            }
            catch { }   // 有些t.GetGizmos会出错，只能这样catch了
            return res;
        }

        public object convert_thing(Thing t, object extra) {
            var d = new Dictionary<string, object> {
                ["id"] = t.thingIDNumber,
                ["type"] = t.GetType().FullName,
                ["name"] = (t as Pawn)?.Name?.ToStringShort ?? t.LabelShort,
                ["fullname"] = (t as Pawn)?.Name?.ToStringFull ?? t.LabelCap,
                ["def"] = t.def,
                ["position"] = t.Position,
                ["faction"] = t.Faction?.ToString(),
                ["commands"] = get_commands(t),
            };
            foreach (var prop in extra.GetType().GetProperties())
                d[prop.Name] = prop.GetValue(extra);
            return d;
        }

        public object PawnToObject(Pawn p) {
            return convert_thing(p, new {
                age = p.ageTracker.AgeBiologicalYears,
                gender = p.gender.ToString(),
                mood = p.needs?.mood?.CurLevelPercentage * 100 ?? -1f,
                health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                hediffs = p.health?.hediffSet?.hediffs,
                currentJob = p.CurJob,
                traits = p.story?.traits?.allTraits.Select(t => t.def) ?? Enumerable.Empty<TraitDef>(),
                workPriorities = DefDatabase<WorkTypeDef>.AllDefs
                    .Select(wt => new { workType = wt, priority = p.workSettings.GetPriority(wt) })
                    .Where(x => x.priority > 0)
                    .OrderBy(x => x.priority)
            });
        }

        public object PawnToDetailedObject(Pawn p) {
            return convert_thing(p, new {
                backstory = p.story?.Title ?? "",
                gender = p.gender.ToString(),
                age = p.ageTracker?.AgeBiologicalYears ?? -1,
                lifeStage = p.ageTracker?.CurLifeStage,
                mood = p.needs?.mood?.CurLevelPercentage * 100 ?? -1f,
                comfort = p.GetStatValue(StatDefOf.Comfort, true),
                needs = p.needs?.AllNeeds.Select(n => new { need = n.def, level = n.CurLevelPercentage * 100 }),
                health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                hediffs = p.health?.hediffSet?.hediffs,
                visibleHediffs = p.health?.hediffSet?.hediffs.Where(h => h.Visible),
                bleedingRate = p.health?.hediffSet?.BleedRateTotal ?? 0f,
                isDowned = p.Downed,
                isDrafted = p.Drafted,
                currentJob = p.CurJob,
                thoughts = p.needs?.mood?.thoughts?.memories?.Memories.Select(t => t.def),
                skills = p.skills?.skills.Select(s => new { skill = s.def, level = s.Level, passion = s.passion.ToString() }),
                equipment = p.equipment?.AllEquipmentListForReading.Select(eq => new { def = eq.def, hitPoints = eq.HitPoints }),
                apparel = p.apparel?.WornApparel.Select(a => new { def = a.def, hitPoints = a.HitPoints }),
                inventory = p.inventory?.innerContainer.Select(i => new { def = i.def, count = i.stackCount }),
                assignedArea = p.playerSettings?.AreaRestrictionInPawnCurrentMap?.Label,
                ownedRoom = p.ownership?.OwnedRoom?.ID ?? 0,
                relations = p.relations?.DirectRelations.Select(r => new { def = r.def, other = r.otherPawn?.thingIDNumber })
            });
        }

        public object MechToObject(Pawn p) {
            return convert_thing(p, new {
                kind = p.kindDef,
                health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                hediffs = p.health?.hediffSet?.hediffs,
                currentJob = p.CurJob,
                energy = p.needs?.energy?.CurLevelPercentage * 100 ?? -1f,
            });
        }

        private List<Designator> allDesignatorsCache;

        public List<Designator> GetAllDesignators() {
            if (allDesignatorsCache == null) {
                allDesignatorsCache = DefDatabase<DesignationCategoryDef>.AllDefs
                    .SelectMany(c => c.ResolvedAllowedDesignators)
                    .ToList();
            }
            return allDesignatorsCache;
        }

        private static Dictionary<string, int> CountThings(SlotGroup group) {
            if (group == null) return new Dictionary<string, int>();
            return group.HeldThings
                .Where(t => !string.IsNullOrEmpty(t.def?.defName))
                .GroupBy(t => t.def.defName)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.stackCount));
        }

        public void input_blueprint(int x, int y, string thing, string stuff, string rotation) {
            ThingDef thingToBuild = DefDatabase<ThingDef>.GetNamed(thing);
            ThingDef stuffToUse = string.IsNullOrEmpty(stuff) ? null : DefDatabase<ThingDef>.GetNamed(stuff);
            IntVec3 desiredPosition = new IntVec3(x, 0, y);
            if (!GenConstruct.CanPlaceBlueprintAt(thingToBuild, desiredPosition, Rot4.FromString(rotation), Find.CurrentMap, stuffDef: stuffToUse))
                throw new Exception("cannot not place blueprint.");
            GenConstruct.PlaceBlueprintForBuild(thingToBuild, desiredPosition, Find.CurrentMap, Rot4.FromString(rotation), Faction.OfPlayer, stuff: stuffToUse);
        }

        public void input_surgery(int id, string recipe, string body_part) {
            Pawn p = get_pawn(id);
            BodyPartRecord bodyPart = p.health.hediffSet.GetNotMissingParts().FirstOrDefault(part => part.Label == body_part);
            if (bodyPart == null) {
                throw new Exception($"Available are {string.Join(", ", p.health.hediffSet.GetNotMissingParts().Select(part => part.Label))}");
            }
            p.health.surgeryBills.AddBill(new Bill_Medical(DefDatabase<RecipeDef>.GetNamed(recipe), null) { Part = bodyPart });
        }

        public int input_create_zone(string label, int x, int y, string type = "Zone_Stockpile") {
            Map map = Find.CurrentMap;
            Zone zone = type == "Zone_Stockpile" ? new Zone_Stockpile(0, map.zoneManager) : (Zone)Activator.CreateInstance(GenTypes.GetTypeInAnyAssembly(type), map.zoneManager);
            zone.label = label;
            zone.AddCell(new IntVec3(x, 0, y));
            map.zoneManager.RegisterZone(zone);
            return zone.ID;
        }

        public void input_zone_add_cell(int zone_id, int x, int y) {
            get_zone(zone_id).AddCell(new IntVec3(x, 0, y));
        }

        public void input_growing_zone_set_allow(int zone_id, bool allow_sow, bool allow_harvest) {
            Zone_Growing zone = get_zone(zone_id) as Zone_Growing;
            zone.allowSow = allow_sow;
            zone.allowCut = allow_harvest;
        }

        public void input_stockpile_zone_disallow_all(int zone_id) {
            (get_zone(zone_id) as Zone_Stockpile).settings.filter.SetDisallowAll();
        }

        public void input_stockpile_zone_allow(int zone_id, string category) {
            (get_zone(zone_id) as Zone_Stockpile).settings.filter.SetAllow(DefDatabase<ThingCategoryDef>.GetNamed(category), true);
        }

        public void input_stockpile_zone_priority(int zone_id, int priority) {
            (get_zone(zone_id) as Zone_Stockpile).settings.Priority = (StoragePriority)priority;
        }

        public void input_pawn_interact(int pawn_id, int target_id, string job) {
            Pawn p = get_pawn(pawn_id);
            Thing target = get_thing(target_id);
            JobDef jd = DefDatabase<JobDef>.GetNamed(job);
            p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jd, target));
        }

        public void input_pawn_interact_biosculpter(int pawn_id, int target_id, string cycle_key) {
            Pawn p = get_pawn(pawn_id);
            Thing target = get_thing(target_id);
            Job j = JobMaker.MakeJob(JobDefOf.EnterBiosculpterPod, target);
            j.biosculpterCycleKey = cycle_key;
            p.jobs.TryTakeOrderedJob(j);
        }

        public void input_pawn_goto(int pawn_id, int x, int y) {
            Pawn p = get_pawn(pawn_id);
            p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, new IntVec3(x, 0, y)));
        }

        public void input_change_mech_control_group(int mech_id, int group_index) {
            Pawn mech = get_pawn(mech_id);
            Pawn overseer = mech.GetOverseer();
            Pawn_MechanitorTracker mechanitor = overseer.mechanitor;
            MechanitorControlGroup targetGroup = mechanitor.controlGroups[group_index];
            targetGroup.Assign(mech);
        }

        public void input_message(string text) {
            Messages.Message(text, MessageTypeDefOf.NeutralEvent, true);
        }

        public string get_command_key(Gizmo g) {
            if (g is Command_Ability ga && !string.IsNullOrEmpty(ga.Ability?.def?.defName))
                return ga.Ability.def.defName;
            if (g is Command_VerbTarget gv && gv.verb != null)
                return gv.verb.GetType().Name;
            if (g is Command c && !string.IsNullOrEmpty(c.icon?.name))
                return c.icon.name;
            return g.GetType().FullName;
        }

        public void input_command(int id, string key, int target_id = 0) {
            Thing p = get_thing(id);
            Thing target_p = target_id != 0 ? get_thing(target_id) : null;
            foreach (Gizmo g in p.GetGizmos()) {
                if (get_command_key(g) != key)
                    continue;
                if (g.Disabled)
                    throw new Exception(string.IsNullOrEmpty(g.disabledReason) ? "command disabled." : g.disabledReason);
                switch (g) {
                    case Command_Toggle g1:
                        g1.toggleAction();
                        return;
                    case Command_Action g2:
                        g2.action();
                        if (target_p != null) {
                            FloatMenu menu = Find.WindowStack.WindowOfType<FloatMenu>() ?? throw new Exception("menu not found.");
                            Find.WindowStack.TryRemove(menu, false);
                            var options = (List<FloatMenuOption>)typeof(FloatMenu).GetField("options", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu);
                            FloatMenuOption opt = options.FirstOrDefault(o => o.iconThing == target_p || o.revalidateClickTarget == target_p) ?? throw new Exception("target not found.");
                            if (opt.Disabled)
                                throw new Exception(opt.Label);
                            opt.action();
                        }
                        return;
                    case Command_VerbTarget g3:
                        g3.verb.TryStartCastOn(target_p);
                        return;
                    case Command_Ability g4:
                        Ability ability = g4.Ability;
                        if (!ability.CanCast)
                            throw new Exception("cannot cast.");
                        LocalTargetInfo l = new LocalTargetInfo(target_p);
                        ability.QueueCastingJob(l, l);
                        return;
                    case Command_Target g5:
                        g5.action(new LocalTargetInfo(target_p));
                        return;
                    default:
                        throw new Exception("command type error.");
                }
            }
            throw new Exception("command not found.");
        }

        public void input_set_forbidden(int id, bool value) {
            get_thing(id).SetForbidden(value);
        }

        public void input_add_designation(int id, string type) {
            Thing target = get_thing(id);
            DesignationManager g = Find.CurrentMap.designationManager;
            Designator dr = GetAllDesignators().FirstOrDefault(d => d.GetType().Name == type);
            if (dr == null) {
                string hint = String.Join(", ", GetAllDesignators().Select(d => d.GetType().Name));
                throw new Exception($"Available are {hint}");
            }
            if (g.HasMapDesignationOn(target))
                g.RemoveAllDesignationsOn(target);
            if (!dr.CanDesignateThing(target)) {
                throw new Exception($"can not designate.");
            }
            dr.DesignateThing(target);
        }

        public void input_remove_all_designation(int id) {
            Find.CurrentMap.designationManager.RemoveAllDesignationsOn(get_thing(id));
        }

        public void input_production_recipe(int id, string recipe, string repeat_mode, int count) {
            RecipeDef r = DefDatabase<RecipeDef>.GetNamed(recipe);
            Building_WorkTable table = get_thing(id) as Building_WorkTable;
            Bill_Production bill = new Bill_Production(r);
            bill.repeatMode = DefDatabase<BillRepeatModeDef>.GetNamed(repeat_mode);
            bill.targetCount = bill.repeatCount = count;
            table.billStack.AddBill(bill);
        }

        #region Data Retrieval (Object Returns)

        public object biosculpter_pods() {
            var field = typeof(CompBiosculpterPod).GetField("biotunedTo", BindingFlags.NonPublic | BindingFlags.Instance);
            return Find.CurrentMap.listerThings.AllThings.Where(t => t.def?.defName == "BiosculpterPod").Select(b => {
                CompBiosculpterPod pod = b.TryGetComp<CompBiosculpterPod>();
                Pawn biotunedTo = field.GetValue(pod) as Pawn;
                return convert_thing(b, new {
                    occupant = pod.Occupant?.thingIDNumber,
                    biotuned_to = biotunedTo?.thingIDNumber,
                    queued_pawn = pod.queuedPawn?.thingIDNumber,
                    current_cycle = pod.CurrentCycle?.Props?.key,
                    state = pod.State.ToString(),
                });
            });
        }

        public object get_production_recipe(int id) {
            return (get_thing(id) as Building_WorkTable).BillStack.Bills.Select(b => new {
                recipe = b.recipe,
                suspended = b.suspended,
            });
        }

        public object things() {
            return Find.CurrentMap.listerThings.AllThings.Select(p => convert_thing(p, new {
                is_forbidden = p.IsForbidden(Faction.OfPlayer),
            }));
        }

        public object zones() {
            return Find.CurrentMap.zoneManager.AllZones.Select(z => {
                var stockpile = z as Zone_Stockpile;
                var growing = z as Zone_Growing;
                return new {
                    id = z.ID,
                    label = z.label,
                    type = z.GetType().Name,
                    cellCount = z.Cells.Count,
                    position = new { x = z.Position.x, z = z.Position.z },
                    priority = stockpile?.settings?.Priority.ToString(),
                    plantToGrow = growing?.GetPlantDefToGrow()?.defName,
                    allowSow = growing?.allowSow,
                    allowCut = growing?.allowCut,
                };
            });
        }

        public object rooms() {
            var map = Find.CurrentMap;
            if (map == null) return new List<object>();
            return map.regionGrid.AllRooms.Select(r => new {
                id = r.ID,
                role = r.Role,
                role_label = r.Role?.label,
                temperature = r.Temperature,
                cell_count = r.CellCount,
                proper_room = r.ProperRoom,
                is_prison_cell = r.IsPrisonCell,
                uses_outdoor_temperature = r.UsesOutdoorTemperature,
                open_roof_count = r.OpenRoofCount,
                owners = r.Owners?.Select(p => p.thingIDNumber)
            });
        }

        public object prisoners() {
            return Find.CurrentMap?.mapPawns?.PrisonersOfColony.Select(PawnToObject);
        }

        public object mechanoids() {
            return Find.CurrentMap?.mapPawns?.PawnsInFaction(Faction.OfPlayer)
                ?.Where(p => p.RaceProps.IsMechanoid)
                .Select(MechToObject);
        }

        public object colony() {
            Map map = Find.CurrentMap;
            return new {
                colonyName = map?.info?.parent?.LabelCap ?? "Unnamed",
                colonistCount = map?.mapPawns?.FreeColonistsCount ?? 0,
                wealth = map?.wealthWatcher?.WealthTotal ?? 0
            };
        }

        public object letters() {
            return Find.LetterStack?.LettersListForReading.Select(l => new {
                label = l.Label.ToString(),
                type = l.def?.letterClass?.ToString(),
                arrivalTime = l.arrivalTime
            });
        }

        public object colonists(bool detail = false) {
            var pawns = Find.CurrentMap?.mapPawns?.FreeColonists;
            if (pawns == null) return new List<object>();
            return detail ? pawns.Select(PawnToDetailedObject) : pawns.Select(PawnToObject);
        }

        public object colonist(int id) {
            return PawnToDetailedObject(get_pawn(id));
        }

        public object map_tiles() {
            var map = Find.CurrentMap;
            var tiles = new List<object>();
            if (map != null)
                for (int x = 0; x < map.Size.x; x++)
                    for (int y = 0; y < map.Size.z; y++)
                        tiles.Add(new { x, y });
            return tiles;
        }

        public object map_tile(int x, int y) {
            Map map = Find.CurrentMap;
            IntVec3 cell = new IntVec3(x, 0, y);
            if (map == null || !cell.InBounds(map)) return new { };
            return new {
                terrain = map.terrainGrid?.TerrainAt(cell),
                zone = map.zoneManager?.ZoneAt(cell)?.label,
                things = map.thingGrid?.ThingsListAt(cell)?.Select(t => t.def).ToList() ?? new List<ThingDef>()
            };
        }

        public object animals() {
            var getSteps = typeof(Pawn_TrainingTracker).GetMethod("GetSteps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return Find.CurrentMap?.mapPawns?.AllPawns
                .Where(p => p.RaceProps?.Animal == true)
                .Select(p => convert_thing(p, new {
                    trainer = p.relations?.DirectRelations
                        .Where(r => r.def == PawnRelationDefOf.Bond)
                        .Select(r => r.otherPawn?.thingIDNumber)
                        .FirstOrDefault(),
                    trainings = DefDatabase<TrainableDef>.AllDefsListForReading.ToDictionary(
                        td => td.defName,
                        td => p.training != null && getSteps != null ? (int)getSteps.Invoke(p.training, new object[] { td }) : 0
                    ),
                    pregnant = p.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) ?? false,
                }));
        }

        public object storage_detail() {
            var zones = (Find.CurrentMap?.zoneManager?.AllZones?.OfType<Zone_Stockpile>() ?? Enumerable.Empty<Zone_Stockpile>())
                .Select(z => new { name = z.label, items = (object)z.slotGroup.HeldThings.Select(t => convert_thing(t, new { stack_count = t.stackCount })) });
            var buildings = (Find.CurrentMap?.listerBuildings?.allBuildingsColonist?.OfType<Building_Storage>() ?? Enumerable.Empty<Building_Storage>())
                .Select(b => new { name = b.LabelCap, items = (object)CountThings(b.slotGroup) });
            return zones.Concat(buildings);
        }

        public object storage() {
            var zones = (Find.CurrentMap?.zoneManager?.AllZones?.OfType<Zone_Stockpile>() ?? Enumerable.Empty<Zone_Stockpile>())
                .Select(z => new { name = z.label, items = CountThings(z.slotGroup) });
            var buildings = (Find.CurrentMap?.listerBuildings?.allBuildingsColonist?.OfType<Building_Storage>() ?? Enumerable.Empty<Building_Storage>())
                .Select(b => new { name = b.LabelCap, items = CountThings(b.slotGroup) });
            return zones.Concat(buildings);
        }

        public object mods() {
            return LoadedModManager.RunningModsListForReading.Select(m => new { name = m.Name, packageId = m.PackageId });
        }

        public object factions() {
            if (Current.ProgramState != ProgramState.Playing || Find.FactionManager == null) return new List<object>();
            return Find.FactionManager.AllFactionsListForReading.Select(f => new {
                name = f.Name,
                def = f.def,
                is_player = f.IsPlayer,
                relation = f.IsPlayer ? "" : (Find.FactionManager?.OfPlayer != null ? Find.FactionManager.OfPlayer.RelationKindWith(f).ToString() : ""),
                goodwill = f.IsPlayer ? 0 : (Find.FactionManager?.OfPlayer?.GoodwillWith(f) ?? 0),
            });
        }

        public object research() {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
                return new { currentProject = string.Empty, progress = 0f, finishedProjects = new List<string>() };

            var manager = Find.ResearchManager;
            ResearchProjectDef current = manager?.GetProject();
            return new {
                currentProject = current?.defName ?? string.Empty,
                progress = current != null ? manager.GetProgress(current) / current.baseCost : 0f,
                finishedProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => p.IsFinished)
            };
        }

        public object buildings() {
            return Find.CurrentMap.listerThings.AllThings.Where(t => t.def.building != null).Select(b => convert_thing(b, new {
                material = b.Stuff,
                rotation = b.Rotation.AsInt,
                is_forbidden = b.IsForbidden(Faction.OfPlayer),
            }));
        }

        public object map() {
            Map map = Find.CurrentMap;
            if (map == null) return new { };
            return new {
                weather = map.weatherManager?.curWeather,
                temperature = map.mapTemperature?.OutdoorTemp ?? 0f,
                hour = GenLocalDate.HourOfDay(map),
                size = map.Size,
                season = GenLocalDate.Season(map).ToString(),
                total_ticks = Find.TickManager.TicksGame,
            };
        }

        public object alerts() {
            if (Current.ProgramState != ProgramState.Playing) return new List<object>();
            try {
                var active = typeof(AlertsReadout).GetField("activeAlerts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Find.Alerts) as IEnumerable<Alert>;
                return active?.Where(a => a.Active).Select(a => new { label = a.Label, priority = a.Priority.ToString() });
            }
            catch { return new List<object>(); }
        }

        public object jobs() {
            return (Find.CurrentMap?.mapPawns?.FreeColonists ?? Enumerable.Empty<Pawn>()).Select(p => new {
                id = p.thingIDNumber,
                name = p.Name.ToStringShort,
                current = p.CurJob?.def,
                queue = p.jobs?.jobQueue?.Select(q => q.job?.def) ?? Enumerable.Empty<JobDef>()
            });
        }

        public object thing_def() {
            return DefDatabase<ThingDef>.AllDefs;
        }

        public object recipe_def() {
            return DefDatabase<RecipeDef>.AllDefs;
        }

        public object bill_repeat_mode_def() {
            return DefDatabase<BillRepeatModeDef>.AllDefs;
        }

        public object thing_category_def() {
            return DefDatabase<ThingCategoryDef>.AllDefs;
        }

        public object job_def() {
            return DefDatabase<JobDef>.AllDefs;
        }

        public string Ping() {
            return "Pong!";
        }
        #endregion
    }

    public static class Server {
        private static HttpListener _listener;
        private static int Port => RimworldAPI_Mod.Settings?.serverPort ?? 18765;
        private static readonly string Prefix = $"http://127.0.0.1:{Port}/";
        private static Thread _thread;
        private static readonly ApiHandler _apiHandler = new ApiHandler();
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new List<JsonConverter> { new RimworldConverter() },
            Formatting = Formatting.Indented
        };
        public static readonly ConcurrentQueue<HttpListenerContext> MainThreadRequestQueue = new ConcurrentQueue<HttpListenerContext>();

        public static void Start() {
            if (_listener != null) return;
            // GenTypes.GetTypeInAnyAssembly("HarmonyMod.Settings")?.GetField("noStacktraceCaching")?.SetValue(null, true);
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();

            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public static void Stop() {
            _listener?.Close();
            _listener = null;
            _thread = null;
        }

        private static async void Loop() {
            while (_listener != null && _listener.IsListening) {
                try {
                    var ctx = await _listener.GetContextAsync();
                    假Handle(ctx);
                }
                catch (Exception ex) {
                    Log.Error($"[RimworldAPI] HttpListener error: {ex}");
                }
            }
        }

        public static void 假Handle(HttpListenerContext ctx) {
            MainThreadRequestQueue.Enqueue(ctx);
        }

        public static void Handle(HttpListenerContext ctx) {
            try {
                lock (ApiHandler.TickSyncLock) {
                    string methodName = ctx.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
                    MethodInfo method = _apiHandler.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (method == null) {
                        WriteResponse(ctx, 404, "application/json", $"\"Method {methodName} not found.\"");
                        return;
                    }

                    var methodParams = method.GetParameters();
                    var args = new object[methodParams.Length];
                    for (int i = 0; i < methodParams.Length; i++) {
                        var param = methodParams[i];
                        string val = ctx.Request.QueryString[param.Name];
                        if (val != null) {
                            args[i] = Convert.ChangeType(val, param.ParameterType);
                        }
                        else {
                            if (!param.IsOptional) {
                                ctx.Response.StatusCode = 400;
                                throw new Exception($"Missing required parameter: {param.Name}");
                            }
                            args[i] = param.DefaultValue;
                        }
                    }

                    object result = method.Invoke(_apiHandler, args);
                    string json = method.ReturnType == typeof(void) ? "\"ok\"" : JsonConvert.SerializeObject(result, JsonSettings);
                    WriteResponse(ctx, 200, "application/json", json);
                }
            }
            catch (Exception ex) {
                int status = ctx.Response.StatusCode == 400 ? 400 : 500;
                string msg = $"【error】: {ex.Message}\n【exceptionType】: {ex.GetType().Name}\n【stackTrace】: {ex.ToString()}";
                WriteResponse(ctx, status, null, msg);
            }
            finally {
                ctx.Response.OutputStream.Close();
            }
        }

        private static void WriteResponse(HttpListenerContext ctx, int statusCode, string contentType, string body) {
            byte[] data = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = statusCode;
            if (contentType != null) ctx.Response.ContentType = contentType;
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
        }
    }
}
