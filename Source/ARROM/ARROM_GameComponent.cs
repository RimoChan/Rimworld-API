using Verse;
using System.Threading;

namespace ARROM
{
    public class ARROM_GameComponent : GameComponent
    {

        public ARROM_GameComponent(Game _) : base()
        {
            // void
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Server.Start();
        }

        public override void GameComponentTick()
        {
            while (Server.MainThreadRequestQueue.TryDequeue(out var ctx))
            {
                Server.Handle(ctx);
            }
        }
    }
}
