using Verse;

namespace ARROM
{
    public class ARROM_GameComponent : GameComponent
    {
        private int tickCounter = 0;

        public ARROM_GameComponent(Game _) : base()
        {
            // void
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Server.Start();
            Server.RefreshCache();
        }

        public override void GameComponentTick()
        {
            tickCounter++;
            if (tickCounter >= ARROM_Mod.Settings.refreshIntervalTicks)
            {
                tickCounter = 0;
                Server.RefreshCache();
            }
        }
    }
}