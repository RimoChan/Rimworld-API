using UnityEngine;
using Verse;

namespace ARROM
{
    public class ARROM_Mod : Mod
    {
        public static ARROM_Settings Settings;

        public ARROM_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ARROM_Settings>();
        }

        public override string SettingsCategory() => "ARROM";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("Port du serveur REST (redémarrage requis) :");
            string bufferPort = Settings.serverPort.ToString();
            list.TextFieldNumeric(ref Settings.serverPort, ref bufferPort, 1, 65535);

            list.Label("Intervalle de rafraîchissement (ticks) :");
            string bufferRefresh = Settings.refreshIntervalTicks.ToString();
            list.TextFieldNumeric(ref Settings.refreshIntervalTicks, ref bufferRefresh, 1);

            list.End();
        }
    }
}
