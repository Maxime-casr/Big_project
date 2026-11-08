using System;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Spells;

namespace BigProject.Core.Session
{
    /// <summary>
    /// Ce que le client voit d'un match. Aujourd'hui : LocalMatchSession (tout en local).
    /// Plus tard : une implémentation réseau qui envoie les plans au serveur et reçoit les RoundResult.
    /// Le reste du client ne dépend que de cette interface.
    /// </summary>
    public interface IMatchSession
    {
        MatchState State { get; }
        SpellCatalog Spells { get; }

        /// <summary>Déclenché quand un tour a été résolu.</summary>
        event Action<RoundResult> RoundResolved;

        bool TrySubmitPlan(TurnPlan plan, out string error);
    }
}
