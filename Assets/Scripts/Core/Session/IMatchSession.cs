using System;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Spells;

namespace BigProject.Core.Session
{
    /// <summary>
    /// Ce que le client voit d'un match. Deux implémentations : LocalMatchSession (hors ligne,
    /// tout en mémoire) et NetworkMatchSession (envoie les plans au serveur, reçoit les résultats).
    /// Le reste du client ne dépend que de cette interface.
    /// </summary>
    public interface IMatchSession
    {
        MatchState State { get; }
        SpellCatalog Spells { get; }

        /// <summary>Secondes restantes pour planifier, 0 si le temps est illimité.</summary>
        float PlanningSecondsRemaining { get; }

        /// <summary>Déclenché quand un tour a été résolu, après mise à jour de State.</summary>
        event Action<RoundResult> RoundResolved;

        /// <summary>Déclenché quand le serveur refuse un plan.</summary>
        event Action<string> PlanRejected;

        bool TrySubmitPlan(TurnPlan plan, out string error);
    }
}
