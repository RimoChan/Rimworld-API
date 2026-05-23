using Verse;

namespace ARROM
{
    public class ARROM_Settings : ModSettings
    {
        public int serverPort = 18765;
        public int refreshIntervalTicks = 300;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref serverPort, "serverPort", 18765);
            Scribe_Values.Look(ref refreshIntervalTicks, "refreshIntervalTicks", 300);
        }
    }
}
