using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using System.Xml;
using Newtonsoft.Json;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ARROM
{        
    public class ApiHandler
    {
        public static readonly object TickSyncLock = new object();

        public Pawn get_pawn(int id) {
            Pawn q = PawnsFinder.AllMaps_Spawned.FirstOrDefault(p => p.thingIDNumber == id);
            if (q == null) {
                throw new Exception($"pawn {id} not found.");
            }
            return q;
        }
        
        public Thing get_thing(int id)
        {
            var q = Find.CurrentMap.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == id);
            if (q == null) {
                throw new Exception($"thing {id} not found.");
            }
            return q;
        }

        private List<Designator> allDesignatorsCache;

        public List<Designator> GetAllDesignators() {
            if (allDesignatorsCache == null)
            {
                allDesignatorsCache = new List<Designator>();
                foreach (DesignationCategoryDef categoryDef in DefDatabase<DesignationCategoryDef>.AllDefs)
                {
                    allDesignatorsCache.AddRange(categoryDef.ResolvedAllowedDesignators);
                }
            }
            return allDesignatorsCache;
        }

        // http://localhost:8765/input_blueprint?x=11&y=11&thing=Wall&stuff=WoodLog
        public void input_blueprint(int x, int y, string thing, string stuff, string rotation) {
            ThingDef thingToBuild = DefDatabase<ThingDef>.GetNamed(thing);
            ThingDef stuffToUse = null;
            if (stuff != "")
                stuffToUse = DefDatabase<ThingDef>.GetNamed(stuff);
            IntVec3 desiredPosition = new IntVec3(x, 0, y);
            Faction playerFaction = Faction.OfPlayer;
            if (!GenConstruct.CanPlaceBlueprintAt(thingToBuild, desiredPosition, Rot4.FromString(rotation), Find.CurrentMap, stuffDef: stuffToUse)) {
                throw new Exception("cannot not place blueprint.");
            }
            GenConstruct.PlaceBlueprintForBuild(thingToBuild, desiredPosition, Find.CurrentMap, Rot4.FromString(rotation), playerFaction, stuff: stuffToUse);
        }

        public void input_command(int id, string label, int target_id)
        {
            Pawn p = get_pawn(id);
            Thing target_p = null;
            if (target_id != 0)
                target_p = get_thing(target_id);
            foreach (Gizmo g in p.GetGizmos())
            {
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

        public void input_set_forbidden(int id, bool value){
            get_thing(id).SetForbidden(value);
        }

        public void input_add_designation(int id, string type){
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

        public string things()
        {
            var result = Find.CurrentMap.listerThings.AllThings.Select(p => new
                {
                    id = p.thingIDNumber,
                    type = p.GetType().FullName,
                    def = p.def?.defName,
                    position = new { x = p.Position.x, y = p.Position.z },
                    is_forbidden = p.IsForbidden(Faction.OfPlayer),
                }
            ).ToList();
            return JsonConvert.SerializeObject(
                result,
                new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter>(),
                    Formatting = Newtonsoft.Json.Formatting.Indented,
                }
            );
        }

        public string animals()
        {
            var animals = Find
                .CurrentMap?.mapPawns?.AllPawns.Where(p => p.RaceProps?.Animal == true)
                .Select(p => new
                {
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
                        td =>
                        {
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

            return JsonConvert.SerializeObject(
                animals,
                new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter>(),
                    Formatting = Newtonsoft.Json.Formatting.Indented,
                }
            );
        }

        public string storage_detail()
        {
            Map map = Find.CurrentMap;
            var storages = new List<object>();

            var zones = map.zoneManager?.AllZones?.OfType<Zone_Stockpile>() ?? Enumerable.Empty<Zone_Stockpile>();
            foreach (var zone in zones)
            {
                var items = zone?.slotGroup.HeldThings.Select(t => new { id = t.thingIDNumber, def = t.def?.defName, stack_count = t.stackCount });
                storages.Add(new { name = zone.label, items });
            }

            var buildings = map.listerBuildings?.allBuildingsColonist?.OfType<Building_Storage>() ?? Enumerable.Empty<Building_Storage>();
            foreach (var building in buildings)
            {
                var items = building?.slotGroup.HeldThings.Select(t => new { id = t.thingIDNumber, def = t.def?.defName, stack_count = t.stackCount });
                storages.Add(new { name = building.LabelCap, items = CountThings(building?.slotGroup)});
            }

            return JsonConvert.SerializeObject(storages, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }


        public string storage()
        {
            Map map = Find.CurrentMap;
            var storages = new List<object>();

            var zones = map.zoneManager?.AllZones?.OfType<Zone_Stockpile>() ?? Enumerable.Empty<Zone_Stockpile>();
            foreach (var zone in zones)
            {
                storages.Add(new { name = zone.label, items = CountThings(zone?.slotGroup) });
            }

            var buildings = map.listerBuildings?.allBuildingsColonist?.OfType<Building_Storage>() ?? Enumerable.Empty<Building_Storage>();
            foreach (var building in buildings)
            {
                storages.Add(new { name = building.LabelCap, items = CountThings(building?.slotGroup)});
            }

            return JsonConvert.SerializeObject(storages, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static Dictionary<string, int> CountThings(SlotGroup group)
        {
            var dict = new Dictionary<string, int>();
            if (group == null)
                return dict;

            foreach (var thing in group.HeldThings)
            {
                string def = thing.def?.defName;
                if (string.IsNullOrEmpty(def))
                    continue;

                if (dict.ContainsKey(def))
                    dict[def] += thing.stackCount;
                else
                    dict[def] = thing.stackCount;
            }

            return dict;
        }


        public string mods()
        {
            var mods = LoadedModManager.RunningModsListForReading
                .Select(m => new { name = m.Name, packageId = m.PackageId });
            return JsonConvert.SerializeObject(mods, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        public string factions()
        {
            // When no game is loaded (e.g. the main menu) Find.FactionManager is
            // null which would cause a NullReferenceException. Return an empty
            // list in that case.
            if (Current.ProgramState != ProgramState.Playing || Find.FactionManager == null)
            {
                return "[]";
            }

            var factions = Find.FactionManager.AllFactionsListForReading
                .Where(f => !f.IsPlayer)
                .Select(f => new
                {
                    name = f.Name,
                    def = f.def?.defName,
                    relation = Find.FactionManager?.OfPlayer != null
                        ? Find.FactionManager.OfPlayer.RelationKindWith(f).ToString()
                        : string.Empty,
                    goodwill = Find.FactionManager?.OfPlayer?.GoodwillWith(f) ?? 0
                })
                .ToList();

            return JsonConvert.SerializeObject(factions, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private IEnumerable<object> gizmo_to_dict(Thing b) {
            try {
                return b.GetGizmos()
                    .Select(m => new {
                        type = m.GetType().FullName,
                        label = (m as Command)?.Label,
                    }).ToList();;
            } catch {
                return null;
            }
        }

        public string research()
        {
            // When no game is loaded (e.g. the main menu), Current.Game is null.
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
            {
                var empty = new
                {
                    currentProject = string.Empty,
                    progress = 0f,
                    finishedProjects = new List<string>()
                };

                return JsonConvert.SerializeObject(empty, new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter>(),
                    Formatting = Newtonsoft.Json.Formatting.Indented
                });
            }

            var manager = Find.ResearchManager;
            ResearchProjectDef current = null;

            if (manager != null)
            {
                // R�cup�re le champ priv� 'currentProj'
                var fld = typeof(ResearchManager)
                    .GetField("currentProj", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fld != null)
                    current = (ResearchProjectDef)fld.GetValue(manager);
            }

            float progress = 0f;
            if (current != null)
                progress = manager.GetProgress(current) / current.baseCost;

            var data = new
            {
                currentProject = current?.defName ?? string.Empty,
                progress,
                finishedProjects = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                    .Where(p => p.IsFinished)
                    .Select(p => p.defName)
                    .ToList()
            };

            return JsonConvert.SerializeObject(data, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }
        
        public string buildings() {
            var result = Find.CurrentMap.listerThings.AllThings.Where(t => t.def.building != null).Select(b => new
            {
                id = b.thingIDNumber,
                type = b.GetType().FullName,
                def = b.def?.defName,
                position = new { x = b.Position.x, y = b.Position.z },
                is_forbidden = b.IsForbidden(Faction.OfPlayer),
                faction = b.Faction?.ToString(),
            }).ToList();
            return JsonConvert.SerializeObject(result, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }
        
        // public string buildings(bool owned)
        // {
        //     IEnumerable<Building> buildings;
        //     if (owned)
        //         buildings = Find.CurrentMap?.listerBuildings?.allBuildingsColonist ?? Enumerable.Empty<Building>();
        //     else 
        //         buildings = Find.CurrentMap?.listerBuildings?.allBuildingsNonColonist ?? Enumerable.Empty<Building>();
        //     var list = buildings.Select(b => new
        //     {
        //         id = b.thingIDNumber,
        //         type = b.GetType().FullName,
        //         def = b.def?.defName,
        //         position = new { x = b.Position.x, y = b.Position.z },
        //         is_forbidden = b.IsForbidden(Faction.OfPlayer),
        //     }).ToList();
        //     return JsonConvert.SerializeObject(list, new JsonSerializerSettings
        //     {
        //         Converters = new List<JsonConverter>(),
        //         Formatting = Newtonsoft.Json.Formatting.Indented
        //     });
        // }

        public string map()
        {
            Map map = Find.CurrentMap;
            if (map == null) return "{}";

            var info = new
            {
                weather = map.weatherManager?.curWeather?.defName,
                temperature = map.mapTemperature?.OutdoorTemp ?? 0f,
                hour = GenLocalDate.HourOfDay(map),
                size = new { x = map.Size.x, y = map.Size.z },
                season = GenLocalDate.Season(map).ToString()    // <-- use GenLocalDate
            };
            
            // for (int x = 0; x < map.Size.x; x++)
            // {
            //     for (int y = 0; y < map.Size.z; y++)

            return JsonConvert.SerializeObject(info, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        public string alerts()
        {
            // When no game is loaded (example on the main menu) Find.Alerts throws
            // an InvalidCastException. Simply return an empty list in that case
            // instead of logging an error.
            if (Current.ProgramState != ProgramState.Playing)
                return "[]";

            var alertsField = typeof(AlertsReadout).GetField("activeAlerts", BindingFlags.Instance | BindingFlags.NonPublic);

            IEnumerable<Alert> active = null;
            try
            {
                active = alertsField?.GetValue(Find.Alerts) as IEnumerable<Alert>;
            }
            catch
            {
                return "[]";
            }

            List<object> list;
            if (active == null)
            {
                list = new List<object>();
            }
            else
            {
                list = active.Where(a => a.Active)
                    .Select(a => (object)new { label = a.Label, priority = a.Priority.ToString() })
                    .ToList();
            }

            return JsonConvert.SerializeObject(list, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        public string jobs()
        {
            var colonists = Find.CurrentMap?.mapPawns?.FreeColonists ?? Enumerable.Empty<Pawn>();

            var list = colonists.Select(p => new
            {
                id = p.thingIDNumber,
                name = p.Name.ToStringShort,
                current = p.CurJob?.def?.defName,
                queue = p.jobs?.jobQueue?.Select(q => q.job?.def?.defName).ToList() ?? new List<string>()
            }).ToList();

            return JsonConvert.SerializeObject(list, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        public string Ping()
        {
            return "Pong!";
        }
    }

    public static class Server
    {
        private static HttpListener _listener;
        private static int Port => ARROM_Mod.Settings?.serverPort ?? 8765;
        private static readonly string Prefix = $"http://+:{Port}/";
        private static Thread _thread;
        private static readonly ApiHandler _apiHandler;

        // Cached JSON responses refreshed on game load
        private static string _cacheColonyInfo = "{}";
        private static string _cacheLetters = "[]";
        private static string _cacheColonists = "[]";
        private static readonly Dictionary<int, string> _cacheColonistsById = new Dictionary<int, string>();
        private static readonly object _cacheLock = new object();

        static Server()
        {
            _apiHandler = new ApiHandler();
        }

        public static void Start()
        {
            if (_listener != null) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);          // ex. http://localhost:8765/
            _listener.Start();

            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
            //Log.Message($"[ARROM] REST API listening on {Prefix}");
        }

        public static void Stop()
        {
            _listener?.Close();
            _listener = null;
            _thread = null;
        }

        public static void RefreshCache()
        {
            lock (_cacheLock)
            {
                _cacheColonyInfo = GetColonyInfo();
                _cacheLetters = GetLetters();
                _cacheColonists = GetColonists();
                // _cacheBuildings = _apiHandler.buildings();

                _cacheColonistsById.Clear();
                var colonists = (Find.CurrentMap?.mapPawns?.FreeColonists ?? Enumerable.Empty<Pawn>()).ToList();

                foreach (var pawn in colonists)
                {
                    _cacheColonistsById[pawn.thingIDNumber] = GetColonistById(pawn.thingIDNumber.ToString());
                }
            }
            //Log.Message("[ARROM] Cache refreshed");
        }

        private static async void Loop()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => Handle(ctx)); // fire-and-forget
                }
                catch (Exception ex)
                {
                    Log.Error($"[ARROM] HttpListener error: {ex}");
                }
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                Monitor.Enter(ApiHandler.TickSyncLock);
                string path = ctx.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
                string json;

                if (path == "colony")
                {
                    json = _cacheColonyInfo;
                }
                else if (path == "letters")
                {
                    json = _cacheLetters;
                }
                else if (path == "colonists")
                {
                    json = _cacheColonists;
                }
                else if (path == "map/tiles")
                {
                    json = GetAllMapTiles();
                }
                else if (path.StartsWith("map/tiles/"))
                {
                    var parts = path.Split('/');
                    // on attend au moins 5 segments : ["", "map", "tiles", "{x}", "{y}", ...]
                    if (parts.Length >= 5
                        && int.TryParse(parts[3], out int tx)
                        && int.TryParse(parts[4], out int ty))
                    {
                        json = GetMapTile(tx, ty);
                    }
                    else
                    {
                        json = "{}";
                    }
                }
                else if (path.StartsWith("colonists/"))
                {
                    string idPart = path.Split('/').Last();
                    if (
                        int.TryParse(idPart, out int cid)
                        && _cacheColonistsById.TryGetValue(cid, out json)
                    )
                    {
                        // json already populated by TryGetValue
                    }
                    else
                    {
                        json = "{}";
                    }
                }
                else
                {
                    string methodName = path;
                    MethodInfo method = _apiHandler
                        .GetType()
                        .GetMethod(
                            methodName,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
                        );
                    if (method == null)
                    {
                        ctx.Response.StatusCode = 404;
                        json = $"\"Method {methodName} not found.\"";
                    }
                    else
                    {
                        var methodParams = method.GetParameters();
                        var queryParams = ctx.Request.QueryString;
                        var args = new object[methodParams.Length];
                        for (int i = 0; i < methodParams.Length; i++) {
                            var param = methodParams[i];
                            string paramValueStr = queryParams.Get(param.Name);

                            if (paramValueStr == null)
                            {
                                if (!param.IsOptional)
                                {
                                    ctx.Response.StatusCode = 400;
                                    json = $"\"Missing required parameter: {param.Name}\"";
                                }
                                args[i] = param.DefaultValue;
                            }
                            else
                            {
                                args[i] = Convert.ChangeType(
                                    paramValueStr,
                                    param.ParameterType
                                );
                            }
                        }
                        object result = method.Invoke(_apiHandler, args);
                        if (method.ReturnType == typeof(void))
                            json = "\"ok\"";
                        else
                            json = (result as string);
                    }
                }

                byte[] data = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentEncoding = Encoding.UTF8;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
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

        #region Endpoints

        private static string GetColonyInfo()
        {
            Map map = Find.CurrentMap;
            var info = new
            {
                colonyName = map?.info?.parent?.LabelCap ?? "Unnamed",
                colonistCount = map?.mapPawns?.FreeColonistsCount ?? 0,
                wealth = map?.wealthWatcher?.WealthTotal ?? 0
            };
            return JsonConvert.SerializeObject(info, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetLetters()
        {
            var letters = Find.LetterStack?.LettersListForReading
                .Select(l => new
                {
                    label = l.GetType().GetProperty("LabelCap")?.GetValue(l)?.ToString(),
                    type = l.def?.letterClass?.ToString(),
                    arrivalTime = l.arrivalTime
                })
                .ToList();

            return JsonConvert.SerializeObject(letters, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetColonists()
        {
            var colonists = Find.CurrentMap?.mapPawns?.FreeColonists
                .Select(p => new
                {
                    id = p.thingIDNumber,
                    name = p.Name.ToStringShort,
                    age = p.ageTracker.AgeBiologicalYears,
                    gender = p.gender.ToString(),
                    // position tile
                    position = new { x = p.Position.x, y = p.Position.z },
                    // humeur et sant�
                    mood = p.needs?.mood?.CurLevelPercentage * 100 ?? -1f,
                    health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                    // job en cours
                    currentJob = p.CurJob?.def?.defName ?? "",
                    // traits
                    traits = p.story?.traits?.allTraits.Select(t => t.def.defName).ToList() ?? new List<string>(),
                    // priorit�s de travail (uniquement celles > 0)
                    workPriorities = DefDatabase<WorkTypeDef>.AllDefs
                        .Select(wt => new { workType = wt.defName, priority = p.workSettings.GetPriority(wt) })
                        .Where(x => x.priority > 0)
                        .OrderBy(x => x.priority)
                        .ToList()
                })
                .ToList();

            return JsonConvert.SerializeObject(colonists, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetColonistById(string idStr)
        {
            if (!int.TryParse(idStr, out int id))
                return "{}";
            Pawn p = Find.CurrentMap?.mapPawns?.FreeColonists.FirstOrDefault(x =>
                x.thingIDNumber == id
            );
            if (p == null)
                return "{}";

            var detail = new
            {
                id,
                commands = p.GetGizmos()
                    .Select(m => new {
                        type = m.GetType().FullName,
                        label = (m as Command)?.Label,
                    }),
                name = p.Name.ToStringFull,
                backstory = p.story?.Title ?? "",
                gender = p.gender.ToString(),
                age = p.ageTracker?.AgeBiologicalYears ?? -1,
                lifeStage = p.ageTracker?.CurLifeStage?.defName ?? "",
                mood = p.needs?.mood?.CurLevelPercentage * 100 ?? -1f,
                comfort = p.GetStatValue(StatDefOf.Comfort, true),
                needs = p.needs?.AllNeeds
                    .Select(n => new { need = n.def.defName, level = n.CurLevelPercentage * 100 })
                    .ToList(),
                health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                hediffs = p.health?.hediffSet?.hediffs
                    .Select(h => new { def = h.def.defName, severity = h.Severity })
                    .ToList(),
                visibleHediffs = p.health?.hediffSet?.hediffs
                    .Where(h => h.Visible)
                    .Select(h => new { def = h.def.defName, severity = h.Severity })
                    .ToList(),
                bleedingRate = p.health?.hediffSet?.BleedRateTotal ?? 0f,
                isDowned = p.Downed,
                isDrafted = p.Drafted,
                // **ici on passe par la propri�t� Pawn.CurJob, et non jobs.CurJob**
                currentJob = p.CurJob?.def?.defName ?? "",
                thoughts = p.needs?.mood?.thoughts?.memories?.Memories
                    .Select(t => t.def.defName)
                    .ToList(),
                skills = p.skills?.skills
                    .Select(s => new { skill = s.def.defName, level = s.Level, passion = s.passion.ToString() })
                    .ToList(),
                equipment = p.equipment?.AllEquipmentListForReading
                    .Select(eq => new { def = eq.def.defName, hitPoints = eq.HitPoints })
                    .ToList(),
                apparel = p.apparel?.WornApparel
                    .Select(a => new { def = a.def.defName, hitPoints = a.HitPoints })
                    .ToList(),
                inventory = p.inventory?.innerContainer
                    .Select(i => new { def = i.def.defName, count = i.stackCount })
                    .ToList(),
                assignedArea = p.playerSettings?.AreaRestrictionInPawnCurrentMap?.Label,
                ownedRoom = p.ownership?.OwnedRoom?.ID ?? 0,
                relations = p.relations?.DirectRelations
                    .Select(r => new { def = r.def.defName, other = r.otherPawn?.thingIDNumber })
                    .ToList()
            };

            return JsonConvert.SerializeObject(detail, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        /*
        private static string GetCaravans()
        {
            var caravans = Find.WorldObjects?.Caravans ?? new List<Caravan>();

            var list = caravans
                .Where(c => c.IsPlayerControlled)
                .Select(c => new
                {
                    id = c.ID,
                    name = c.LabelCap,
                    tile = c.Tile,
                    pawns = c.PawnsListForReading.Select(p => p.thingIDNumber).ToList()
                })
                .ToList();

            return JsonConvert.SerializeObject(list, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }
        */
        

        private static string GetAllMapTiles()
        {
            var map = Find.CurrentMap;
            if (map == null) return "[]";

            // borne maximale : map.Size.x, map.Size.z
            var tiles = new List<object>();
            for (int x = 0; x < map.Size.x; x++)
            {
                for (int y = 0; y < map.Size.z; y++)
                {
                    tiles.Add(new { x, y });
                }
            }

            return JsonConvert.SerializeObject(tiles, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetMapTile(int x, int y)
        {
            Map map = Find.CurrentMap;
            if (map == null) return "{}";

            IntVec3 cell = new IntVec3(x, 0, y);
            if (!cell.InBounds(map)) return "{}";

            var info = new
            {
                terrain = map.terrainGrid?.TerrainAt(cell)?.defName,
                zone = map.zoneManager?.ZoneAt(cell)?.label,
                things = map.thingGrid?.ThingsListAt(cell)?.Select(t => t.def?.defName).ToList() ?? new List<string>()
            };

            return JsonConvert.SerializeObject(info, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        #endregion
    }
}
