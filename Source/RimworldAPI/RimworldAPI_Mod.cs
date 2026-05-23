using UnityEngine;
using Verse;

namespace RimworldAPI
{
    public class RimworldAPI_Mod : Mod
    {
        public static RimworldAPI_Settings Settings;

        public RimworldAPI_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimworldAPI_Settings>();
        }

        public override string SettingsCategory() => "RimworldAPI";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("RimworldAPI.ServerPortLabel".Translate());
            string bufferPort = Settings.serverPort.ToString();
            list.TextFieldNumeric(ref Settings.serverPort, ref bufferPort, 1, 65535);

            list.Label("RimworldAPI.RefreshIntervalLabel".Translate());
            string bufferRefresh = Settings.refreshIntervalTicks.ToString();
            list.TextFieldNumeric(ref Settings.refreshIntervalTicks, ref bufferRefresh, 1);

            list.End();
        }
    }
}
