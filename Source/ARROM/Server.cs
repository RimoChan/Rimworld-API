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

namespace ARROM {
    public class ApiHandler {
        public static readonly object TickSyncLock = new object();

        public Pawn get_pawn(int id) {
            Pawn q = PawnsFinder.AllMaps_Spawned.FirstOrDefault(p => p.thingIDNumber == id);
            if (q == null) {
                throw new Exception($"pawn {id} not found.");
            }
            return q;
        }

        public Thing get_thing(int id) {
            var q = Find.CurrentMap.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == id);
            if (q == null) {
                throw new Exception($"thing {id} not found.");
            }
            return q;
        }

        public Zone get_zone(int id) {
            Map map = Find.CurrentMap;
            Zone zone = map.zoneManager.AllZones.FirstOrDefault(z => z.ID == id);
            if (zone == null)
                throw new Exception("zone not found.");
            return zone;
        }

        public object PawnToObject(Pawn p) {
            return new {
                id = p.thingIDNumber,
                name = p.Name.ToStringShort,
                age = p.ageTracker.AgeBiologicalYears,
                gender = p.gender.ToString(),
                position = new { x = p.Position.x, y = p.Position.z },
                mood = p.needs?.mood?.CurLevelPercentage * 100 ?? -1f,
                health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                hediff = p.health?.hediffSet?.hediffs?.Select(x => new { part = x.Part?.Label, label = x.Label }).ToList(),
                currentJob = p.CurJob?.def?.defName ?? "",
                traits = p.story?.traits?.allTraits.Select(t => t.def.defName).ToList() ?? new List<string>(),
                workPriorities = DefDatabase<WorkTypeDef>.AllDefs
                    .Select(wt => new { workType = wt.defName, priority = p.workSettings.GetPriority(wt) })
                    .Where(x => x.priority > 0)
                    .OrderBy(x => x.priority)
                    .ToList()
            };
        }

        public object PawnToDetailedObject(Pawn p) {
            return new {
                id = p.thingIDNumber,
                commands = p.GetGizmos().Select(m => new { type = m.GetType().FullName, label = (m as Command)?.Label }),
                name = p.Name.ToStringFull,
                backstory = p.story?.Title ?? "",
                gender = p.gender.ToString(),
                age = p.ageTracker?.AgeBiologicalYears ?? -1,
                lifeStage = p.ageTracker?.CurLifeStage?.defName ?? "",
                mood = p.needs?.mood?.CurLevelPercentage * 100 ?? -1f,
                comfort = p.GetStatValue(StatDefOf.Comfort, true),
                needs = p.needs?.AllNeeds.Select(n => new { need = n.def.defName, level = n.CurLevelPercentage * 100 }).ToList(),
                health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                hediffs = p.health?.hediffSet?.hediffs.Select(h => new { def = h.def.defName, severity = h.Severity }).ToList(),
                visibleHediffs = p.health?.hediffSet?.hediffs.Where(h => h.Visible).Select(h => new { def = h.def.defName, severity = h.Severity }).ToList(),
                bleedingRate = p.health?.hediffSet?.BleedRateTotal ?? 0f,
                isDowned = p.Downed,
                isDrafted = p.Drafted,
                currentJob = p.CurJob?.def?.defName ?? "",
                thoughts = p.needs?.mood?.thoughts?.memories?.Memories.Select(t => t.def.defName).ToList(),
                skills = p.skills?.skills.Select(s => new { skill = s.def.defName, level = s.Level, passion = s.passion.ToString() }).ToList(),
                equipment = p.equipment?.AllEquipmentListForReading.Select(eq => new { def = eq.def.defName, hitPoints = eq.HitPoints }).ToList(),
                apparel = p.apparel?.WornApparel.Select(a => new { def = a.def.defName, hitPoints = a.HitPoints }).ToList(),
                inventory = p.inventory?.innerContainer.Select(i => new { def = i.def.defName, count = i.stackCount }).ToList(),
                assignedArea = p.playerSettings?.AreaRestrictionInPawnCurrentMap?.Label,
                ownedRoom = p.ownership?.OwnedRoom?.ID ?? 0,
                relations = p.relations?.DirectRelations.Select(r => new { def = r.def.defName, other = r.otherPawn?.thingIDNumber }).ToList()
            };
        }

        public object MechToObject(Pawn p) {
            return new {
                id = p.thingIDNumber,
                name = p.Name?.ToStringShort ?? p.LabelShort,
                kind = p.kindDef.defName,
                position = new { x = p.Position.x, y = p.Position.z },
                health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                hediff = p.health?.hediffSet?.hediffs?.Select(x => new { part = x.Part?.Label, label = x.Label }).ToList(),
                currentJob = p.CurJob?.def?.defName ?? "",
                energy = p.needs?.energy?.CurLevelPercentage * 100 ?? -1f,
            };
        }

        private List<Designator> allDesignatorsCache;

        public List<Designator> GetAllDesignators() {
            if (allDesignatorsCache == null) {
                allDesignatorsCache = new List<Designator>();
                foreach (DesignationCategoryDef categoryDef in DefDatabase<DesignationCategoryDef>.AllDefs)
                    allDesignatorsCache.AddRange(categoryDef.ResolvedAllowedDesignators);
            }
            return allDesignatorsCache;
        }

        private static Dictionary<string, int> CountThings(SlotGroup group) {
            var dict = new Dictionary<string, int>();
            if (group == null) return dict;
            foreach (var thing in group.HeldThings) {
                string def = thing.def?.defName;
                if (string.IsNullOrEmpty(def)) continue;
                dict[def] = dict.ContainsKey(def) ? dict[def] + thing.stackCount : thing.stackCount;
            }
            return dict;
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

        public string input_create_zone(string label, int x, int y) {
            Map map = Find.CurrentMap;
            Zone_Stockpile zone = new Zone_Stockpile(0, map.zoneManager);
            zone.label = label;
            zone.AddCell(new IntVec3(x, 0, y));
            map.zoneManager.RegisterZone(zone);
            return $"\"{zone.ID}\"";
        }

        public void input_zone_add_cell(int zone_id, int x, int y) {
            Map map = Find.CurrentMap;
            Zone zone = map.zoneManager.AllZones.FirstOrDefault(z => z.ID == zone_id);
            zone.AddCell(new IntVec3(x, 0, y));
        }

        public void input_stockpile_zone_disallow_all(int zone_id) {
            Zone_Stockpile zone = get_zone(zone_id) as Zone_Stockpile;
            StorageSettings settings = zone.settings;
            ThingFilter filter = settings.filter;
            filter.SetDisallowAll();
        }

        public void input_stockpile_zone_allow(int zone_id, string category) {
            Zone_Stockpile zone = get_zone(zone_id) as Zone_Stockpile;
            StorageSettings settings = zone.settings;
            ThingFilter filter = settings.filter;
            filter.SetAllow(DefDatabase<ThingCategoryDef>.GetNamed(category), true);
        }

        public void input_stockpile_zone_priority(int zone_id, int priority) {
            Zone_Stockpile zone = get_zone(zone_id) as Zone_Stockpile;
            StorageSettings settings = zone.settings;
            settings.Priority = (StoragePriority)priority;
        }

        public void input_pawn_interact(int pawn_id, int target_id, string job) {
            Pawn p = get_pawn(pawn_id);
            Thing target = get_thing(target_id);
            JobDef jd = DefDatabase<JobDef>.GetNamed(job);
            p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jd, target));
        }

        public void input_pawn_interact_biosculpter(int pawn_id, int target_id, string cycle_key) {
            // Available cycle_key: medic,bioregeneration,ageReversal,pleasure
            Pawn p = get_pawn(pawn_id);
            Thing target = get_thing(target_id);
            Job j = JobMaker.MakeJob(JobDefOf.EnterBiosculpterPod, target);
            j.biosculpterCycleKey = cycle_key;
            p.jobs.TryTakeOrderedJob(j);
        }

        public void input_message(string text) {
            Messages.Message(text, MessageTypeDefOf.NeutralEvent, true);
        }

        public void input_command(int id, string label, int target_id) {
            Pawn p = get_pawn(id);
            Thing target_p = null;
            if (target_id != 0)
                target_p = get_thing(target_id);
            foreach (Gizmo g in p.GetGizmos()) {
                if ((g as Command)?.Label != label && g.GetType().FullName != label)    // 开火命令没有label，只能这样代替1下了
                    continue;
                if (g is Command_Toggle g1) {
                    g1.toggleAction();
                    return;
                }
                if (g is Command_Action g2) {
                    g2.action();
                    return;
                }
                if (g is Command_VerbTarget g3) {
                    g3.verb.TryStartCastOn(target_p);
                    return;
                }
                if (g is Command_Ability g4) {
                    Ability ability = g4.Ability;
                    LocalTargetInfo l = new LocalTargetInfo(target_p);
                    if (!ability.CanCast) {
                        throw new Exception("cannot cast.");
                    }
                    ability.QueueCastingJob(l, l);
                    return;
                }
                throw new Exception("command type error.");
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

        public void input_remove_all_designation(int id){
            Thing target = get_thing(id);
            Find.CurrentMap.designationManager.RemoveAllDesignationsOn(target);
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
            return Find.CurrentMap.listerThings.AllThings.Where(t => t.def?.defName == "BiosculpterPod").Select(b => {
                CompBiosculpterPod pod = b.TryGetComp<CompBiosculpterPod>();
                Pawn biotunedTo = pod.GetType().GetField("biotunedTo", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pod) as Pawn;
                return new {
                    id=b.thingIDNumber,
                    occupant=pod.Occupant?.thingIDNumber,
                    biotuned_to=biotunedTo?.thingIDNumber,
                    queued_pawn=pod.queuedPawn?.thingIDNumber,
                    current_cycle=pod.CurrentCycle?.Props?.key,
                    state=pod.State.ToString(),
                };
            });
        }

        public object get_production_recipe(int id) {
            Building_WorkTable bg = get_thing(id) as Building_WorkTable;
            return bg.BillStack.Bills.Select(b => new {
                recipe=b.recipe.defName,
                suspended=b.suspended,
            });
        }

        public object things() {
            var result = Find.CurrentMap.listerThings.AllThings.Select(p => new {
                    id = p.thingIDNumber,
                    type = p.GetType().FullName,
                    def = p.def?.defName,
                    position = new { x = p.Position.x, y = p.Position.z },
                    is_forbidden = p.IsForbidden(Faction.OfPlayer),
                }
            ).ToList();
            return result;
        }
        
        public object zones() {
            return Find.CurrentMap.zoneManager.AllZones.Select(z => new { id = z.ID, label = z.label });
        }

        public object prisoners() {
            var ps = Find.CurrentMap?.mapPawns?.PrisonersOfColony.Select(PawnToObject).ToList();
            return ps;
        }

        public object mechanoids() {
            var mechs = Find.CurrentMap?.mapPawns?.PawnsInFaction(Faction.OfPlayer)
                ?.Where(p => p.RaceProps.IsMechanoid)
                .Select(MechToObject)
                .ToList();
            return mechs;
        }

        public object colony() {
            Map map = Find.CurrentMap;
            return new {
                colonyName = map?.info?.parent?.LabelCap ?? "Unnamed",
                colonistCount = map?.mapPawns?.FreeColonistsCount ?? 0,
                wealth = map?.wealthWatcher?.WealthTotal ?? 0
            };
        }

        public object letters() => Find.LetterStack?.LettersListForReading.Select(l => new {
            label = l.GetType().GetProperty("LabelCap")?.GetValue(l)?.ToString(),
            type = l.def?.letterClass?.ToString(),
            arrivalTime = l.arrivalTime
        }).ToList();

        public object colonists(bool detail = false) {
            var pawns = Find.CurrentMap?.mapPawns?.FreeColonists;
            if (pawns == null) return new List<object>();
            return detail 
                ? pawns.Select(PawnToDetailedObject).ToList() 
                : pawns.Select(PawnToObject).ToList();
        }

        public object colonist(int id) {
            Pawn p = get_pawn(id);
            return PawnToDetailedObject(p);
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
                terrain = map.terrainGrid?.TerrainAt(cell)?.defName,
                zone = map.zoneManager?.ZoneAt(cell)?.label,
                things = map.thingGrid?.ThingsListAt(cell)?.Select(t => t.def?.defName).ToList() ?? new List<string>()
            };
        }


        public object animals() {
            var animals = Find
                .CurrentMap?.mapPawns?.AllPawns.Where(p => p.RaceProps?.Animal == true)
                .Select(p => new {
                    id = p.thingIDNumber,
                    name = p.LabelShortCap,
                    def = p.def?.defName,
                    faction = p.Faction?.ToString(),
                    position = new { x = p.Position.x, y = p.Position.z },
                    trainer = p
                        .relations?.DirectRelations.Where(r => r.def == PawnRelationDefOf.Bond)
                        .Select(r => r.otherPawn?.thingIDNumber)
                        .FirstOrDefault(),
                    trainings = DefDatabase<TrainableDef>.AllDefsListForReading.ToDictionary(
                        td => td.defName,
                        td => {
                            if (p.training == null)
                                return 0;
                            var mi = typeof(Pawn_TrainingTracker).GetMethod(
                                "GetSteps",
                                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                            );
                            return mi != null ? (int)mi.Invoke(p.training, new object[] { td }) : 0;
                        }
                    ),
                    pregnant = p.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) ?? false,
                })
                .ToList();

            return animals;
        }

        public object storage_detail() {
            var storages = new List<object>();
            foreach (var zone in Find.CurrentMap.zoneManager?.AllZones?.OfType<Zone_Stockpile>() ?? Enumerable.Empty<Zone_Stockpile>())
                storages.Add(new { name = zone.label, items = zone?.slotGroup.HeldThings.Select(t => new { id = t.thingIDNumber, def = t.def?.defName, stack_count = t.stackCount }) });
            foreach (var building in Find.CurrentMap.listerBuildings?.allBuildingsColonist?.OfType<Building_Storage>() ?? Enumerable.Empty<Building_Storage>())
                storages.Add(new { name = building.LabelCap, items = CountThings(building?.slotGroup) });
            return storages;
        }

        public object storage() {
            var storages = new List<object>();
            foreach (var zone in Find.CurrentMap.zoneManager?.AllZones?.OfType<Zone_Stockpile>() ?? Enumerable.Empty<Zone_Stockpile>())
                storages.Add(new { name = zone.label, items = CountThings(zone?.slotGroup) });
            foreach (var building in Find.CurrentMap.listerBuildings?.allBuildingsColonist?.OfType<Building_Storage>() ?? Enumerable.Empty<Building_Storage>())
                storages.Add(new { name = building.LabelCap, items = CountThings(building?.slotGroup) });
            return storages;
        }

        public object mods() => LoadedModManager.RunningModsListForReading.Select(m => new { name = m.Name, packageId = m.PackageId });

        public object factions() {
            if (Current.ProgramState != ProgramState.Playing || Find.FactionManager == null) return new List<object>();
            return Find.FactionManager.AllFactionsListForReading.Select(f => new {
                name = f.Name, def = f.def?.defName, is_player = f.IsPlayer,
                relation = f.IsPlayer ? "" : (Find.FactionManager?.OfPlayer != null ? Find.FactionManager.OfPlayer.RelationKindWith(f).ToString() : ""),
                goodwill = f.IsPlayer ? 0 : (Find.FactionManager?.OfPlayer?.GoodwillWith(f) ?? 0),
            }).ToList();
        }

        public object research() {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
                return new { currentProject = string.Empty, progress = 0f, finishedProjects = new List<string>() };

            var manager = Find.ResearchManager;
            ResearchProjectDef current = manager != null ? (ResearchProjectDef)typeof(ResearchManager).GetField("currentProj", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager) : null;
            return new {
                currentProject = current?.defName ?? string.Empty,
                progress = current != null ? manager.GetProgress(current) / current.baseCost : 0f,
                finishedProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => p.IsFinished).Select(p => p.defName).ToList()
            };
        }

        public object buildings() => Find.CurrentMap.listerThings.AllThings.Where(t => t.def.building != null).Select(b => new {
            id = b.thingIDNumber,
            type = b.GetType().FullName,
            def = b.def?.defName,
            material = b.Stuff?.defName, // 新增：建筑的材料 (例如: "WoodLog", "Steel")。如果建筑没有自选材料则为 null
            position = new { x = b.Position.x, y = b.Position.z },
            rotation = b.Rotation.AsInt, // 新增：建筑的朝向 (0: 北, 1: 东, 2: 南, 3: 西)
            is_forbidden = b.IsForbidden(Faction.OfPlayer),
            faction = b.Faction?.ToString()
        }).ToList();

        public object map() {
            Map map = Find.CurrentMap;
            if (map == null) return new { };
            return new {
                weather = map.weatherManager?.curWeather?.defName,
                temperature = map.mapTemperature?.OutdoorTemp ?? 0f,
                hour = GenLocalDate.HourOfDay(map),
                size = new { x = map.Size.x, y = map.Size.z },
                season = GenLocalDate.Season(map).ToString(),
                total_ticks = Find.TickManager.TicksGame,
            };
        }

        public object alerts() {
            if (Current.ProgramState != ProgramState.Playing) return new List<object>();
            try
            {
                var active = typeof(AlertsReadout).GetField("activeAlerts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Find.Alerts) as IEnumerable<Alert>;
                return active?.Where(a => a.Active).Select(a => new { label = a.Label, priority = a.Priority.ToString() }).ToList();
            }
            catch { return new List<object>(); }
        }

        public object jobs() => (Find.CurrentMap?.mapPawns?.FreeColonists ?? Enumerable.Empty<Pawn>()).Select(p => new {
            id = p.thingIDNumber, name = p.Name.ToStringShort, current = p.CurJob?.def?.defName, queue = p.jobs?.jobQueue?.Select(q => q.job?.def?.defName).ToList() ?? new List<string>()
        }).ToList();

        public object thing_def() {
            return DefDatabase<ThingDef>.AllDefs.Select(i => i.defName);
        }

        public object recipe_def() {
            return DefDatabase<RecipeDef>.AllDefs.Select(i => i.defName).ToList();
        }

        public string Ping() {
            return "Pong!";
        }
        #endregion
    }
    public static class Server
    {
        private static HttpListener _listener;
        private static int Port => ARROM_Mod.Settings?.serverPort ?? 8765;
        private static readonly string Prefix = $"http://+:{Port}/";
        private static Thread _thread;
        private static readonly ApiHandler _apiHandler = new ApiHandler();
        public static readonly ConcurrentQueue<HttpListenerContext> MainThreadRequestQueue = new ConcurrentQueue<HttpListenerContext>();


        public static void Start() {
            if (_listener != null) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();

            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
            //Log.Message($"[ARROM] REST API listening on {Prefix}");
        }

        public static void Stop() {
            _listener?.Close();
            _listener = null;
            _thread = null;
        }

        private static async void Loop() {
            while (_listener != null && _listener.IsListening) {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    假Handle(ctx);
                }
                catch (Exception ex) {
                    Log.Error($"[ARROM] HttpListener error: {ex}");
                }
            }
        }

        public static void 假Handle(HttpListenerContext ctx) {
            MainThreadRequestQueue.Enqueue(ctx);
        }

        public static void Handle(HttpListenerContext ctx) {
            try
            {
                Monitor.Enter(ApiHandler.TickSyncLock);
                string path = ctx.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
                string methodName = path;
                string json;

                var argsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in ctx.Request.QueryString.AllKeys) {
                    if (key != null) argsDict[key] = ctx.Request.QueryString[key];
                }
                MethodInfo method = _apiHandler.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (method == null) {
                    ctx.Response.StatusCode = 404;
                    json = $"\"Method {methodName} not found.\"";
                }
                else {
                    var methodParams = method.GetParameters();
                    var args = new object[methodParams.Length];
                    for (int i = 0; i < methodParams.Length; i++) {
                        var param = methodParams[i];
                        if (argsDict.TryGetValue(param.Name, out string paramValueStr)) {
                            args[i] = Convert.ChangeType(paramValueStr, param.ParameterType);
                        }
                        else {
                            if (!param.IsOptional) { ctx.Response.StatusCode = 400; throw new Exception($"Missing required parameter: {param.Name}"); }
                            args[i] = param.DefaultValue;
                        }
                    }

                    object result = method.Invoke(_apiHandler, args);
                    if (method.ReturnType == typeof(void))
                        json = "\"ok\"";
                    else
                        json = JsonConvert.SerializeObject(result, new JsonSerializerSettings {
                                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                                Converters = new List<JsonConverter>(),
                                Formatting = Formatting.Indented
                            }
                        );
                }

                byte[] data = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentEncoding = Encoding.UTF8;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            }
            catch (Exception ex) {
                string msg = $"【error】: {ex.Message}\n【exceptionType】: {ex.GetType().Name}\n【stackTrace】: {ex.ToString()}";
                byte[] data = Encoding.UTF8.GetBytes(msg);
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentEncoding = Encoding.UTF8;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            }
            finally
            {
                Monitor.Exit(ApiHandler.TickSyncLock);
                ctx.Response.OutputStream.Close();
            }
        }
    }
}
