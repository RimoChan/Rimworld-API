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
    public static class Server
    {
        private static HttpListener _listener;
        private static int Port => ARROM_Mod.Settings?.serverPort ?? 8765;
        private static readonly string Prefix = $"http://localhost:{Port}/";
        private static Thread _thread;

        // Cached JSON responses refreshed on game load
        private static string _cacheColonyInfo = "{}";
        private static string _cacheLetters = "[]";
        private static string _cacheColonists = "[]";
        private static readonly Dictionary<int, string> _cacheColonistsById = new Dictionary<int, string>();
        private static readonly object _cacheLock = new object();

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
                    _ = Task.Run(() => Handle(ctx));   // fire-and-forget
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
                string path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                //Log.Message($"[ARROM] Received {ctx.Request.HttpMethod} {path}");
                string json;

                if (path == "/colony")
                {
                    json = _cacheColonyInfo;
                }
                else if (path == "/letters")
                {
                    json = _cacheLetters;
                }
                else if (path == "/colonists")
                {
                    json = _cacheColonists;
                }
                else if (path == "/mods")
                {
                    json = GetMods();
                }
                else if (path == "/factions")
                {
                    json = GetFactions();
                }
                else if (path == "/research")
                {
                    json = GetResearch();
                }
                else if (path.StartsWith("/buildings"))
                {
                    string type = ctx.Request.QueryString["type"];
                    json = GetBuildings(type);
                }
                else if (path == "/map")
                {
                    json = GetMapInfo();
                }
                else if (path == "/animals")
                {
                    json = GetAnimals();
                }
                /*
                else if (path == "/caravans")
                {
                    json = GetCaravans();
                }
                */
                else if (path == "/storage")
                {
                    json = GetStorage();
                }
                else if (path == "/alerts")
                {
                    json = GetAlerts();
                }
                else if (path == "/jobs")
                {
                    json = GetJobs();
                }
                else if (path == "/map/tiles")
                {
                    json = GetAllMapTiles();
                }
                else if (path.StartsWith("/map/tiles/"))
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
                else if (path.StartsWith("/colonists/"))
                {
                    string idPart = path.Split('/').Last();
                    if (int.TryParse(idPart, out int cid) && _cacheColonistsById.TryGetValue(cid, out json))
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
                    Log.Warning($"[ARROM] Unknown endpoint {path}");
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                byte[] data = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentEncoding = Encoding.UTF8;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                //Log.Message($"[ARROM] Sent {data.Length} bytes for {path}");
            }
            catch (Exception ex)
            {
                Log.Error($"[ARROM] Request error: {ex}");
                ctx.Response.StatusCode = 500;
            }
            finally
            {
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
                    // humeur et santé
                    mood = p.needs?.mood?.CurLevelPercentage * 100 ?? -1f,
                    health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                    // job en cours
                    currentJob = p.CurJob?.def?.defName ?? "",
                    // traits
                    traits = p.story?.traits?.allTraits.Select(t => t.def.defName).ToList() ?? new List<string>(),
                    // priorités de travail (uniquement celles > 0)
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
            if (!int.TryParse(idStr, out int id)) return "{}";
            Pawn p = Find.CurrentMap?.mapPawns?.FreeColonists.FirstOrDefault(x => x.thingIDNumber == id);
            if (p == null) return "{}";

            var detail = new
            {
                id,
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
                // **ici on passe par la propriété Pawn.CurJob, et non jobs.CurJob**
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

        private static string GetMods()
        {
            var mods = LoadedModManager.RunningModsListForReading
                .Select(m => new { name = m.Name, packageId = m.PackageId });
            return JsonConvert.SerializeObject(mods, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetFactions()
        {
            var factions = Find.FactionManager?.AllFactionsListForReading
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

        private static string GetResearch()
        {
            var manager = Find.ResearchManager;
            ResearchProjectDef current = null;
            if (manager != null)
            {
                // Récupère le champ privé 'currentProj'
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

        private static string GetBuildings(string typeFilter)
        {
            IEnumerable<Building> buildings = Find.CurrentMap?.listerBuildings?.allBuildingsColonist ?? Enumerable.Empty<Building>();
            if (!string.IsNullOrEmpty(typeFilter))
            {
                string filter = typeFilter.ToLowerInvariant();
                buildings = buildings.Where(b => b.def?.defName?.ToLowerInvariant().Contains(filter) ?? false);
            }

            var list = buildings.Select(b => new
            {
                id = b.thingIDNumber,
                def = b.def?.defName,
                position = new { x = b.Position.x, y = b.Position.z }
            }).ToList();

            return JsonConvert.SerializeObject(list, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetMapInfo()
        {
            Map map = Find.CurrentMap;
            if (map == null) return "{}";

            var info = new
            {
                weather = map.weatherManager?.curWeather?.defName,
                temperature = map.mapTemperature?.OutdoorTemp ?? 0f,
                hour = GenLocalDate.HourOfDay(map),
                season = GenLocalDate.Season(map).ToString()    // <-- use GenLocalDate
            };

            return JsonConvert.SerializeObject(info, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetAnimals()
        {
            var animals = Find.CurrentMap?.mapPawns?.AllPawns
                .Where(p => p.Faction == Faction.OfPlayer && p.RaceProps?.Animal == true)
                .Select(p => new
                {
                    id = p.thingIDNumber,
                    name = p.LabelShortCap,
                    def = p.def?.defName,
                    position = new { x = p.Position.x, y = p.Position.z },
                    trainer = p.relations?.DirectRelations
                        .Where(r => r.def == PawnRelationDefOf.Bond)
                        .Select(r => r.otherPawn?.thingIDNumber)
                        .FirstOrDefault(),
                    trainings = DefDatabase<TrainableDef>.AllDefsListForReading
                        .ToDictionary(td => td.defName, td =>
                        {
                            if (p.training == null) return 0;
                            var mi = typeof(Pawn_TrainingTracker).GetMethod("GetSteps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            return mi != null ? (int)mi.Invoke(p.training, new object[] { td }) : 0;
                        }),
                    pregnant = p.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) ?? false
                })
                .ToList();

            return JsonConvert.SerializeObject(animals, new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            });
        }

        private static string GetStorage()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return "[]";
            }

            var storages = new List<object>();

            // Stockpile zones
            var zones = map.zoneManager?.AllZones?.OfType<Zone_Stockpile>() ?? Enumerable.Empty<Zone_Stockpile>();
            foreach (var zone in zones)
            {
                var items = CountThings(zone?.slotGroup);
                storages.Add(new { name = zone.label, items });
            }

            // Storage buildings (e.g. shelves)
            var buildings = map.listerBuildings?.allBuildingsColonist?.OfType<Building_Storage>() ?? Enumerable.Empty<Building_Storage>();
            foreach (var building in buildings)
            {
                var items = CountThings(building?.slotGroup);
                storages.Add(new { name = building.LabelCap, items });
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
            if (group == null) return dict;

            foreach (var thing in group.HeldThings)
            {
                string def = thing.def?.defName;
                if (string.IsNullOrEmpty(def)) continue;

                if (dict.ContainsKey(def))
                    dict[def] += thing.stackCount;
                else
                    dict[def] = thing.stackCount;
            }

            return dict;
        }

        private static string GetAlerts()
        {
            var alertsField = typeof(AlertsReadout).GetField("activeAlerts", BindingFlags.Instance | BindingFlags.NonPublic);
            var active = alertsField?.GetValue(Find.Alerts) as IEnumerable<Alert>;

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

        private static string GetJobs()
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