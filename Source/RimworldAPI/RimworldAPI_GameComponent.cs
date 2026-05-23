using Verse;
using System.Threading;

namespace RimworldAPI
{
    public class RimworldAPI_GameComponent : GameComponent
    {

        public RimworldAPI_GameComponent(Game _) : base()
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
