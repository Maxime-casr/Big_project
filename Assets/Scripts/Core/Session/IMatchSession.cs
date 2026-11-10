using System;
using System.Collections.Generic;
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

        /// <summary>Joueurs qui ont déjà validé leur tour, et nombre de joueurs attendus.</summary>
        int PlansReady { get; }
        int PlansExpected { get; }

        /// <summary>Déclenché quand un tour a été résolu, après mise à jour de State.</summary>
        event Action<RoundResult> RoundResolved;

        /// <summary>Déclenché quand le serveur refuse un plan.</summary>
        event Action<string> PlanRejected;

        bool TrySubmitPlan(TurnPlan plan, out string error);

        /// <summary>
        /// Partage le brouillon du joueur avec ses alliés pendant la planification (en ligne seulement,
        /// jamais avec les ennemis). Sans effet hors ligne.
        /// </summary>
        void SendDraft(IReadOnlyList<UnitPlan> units);

        /// <summary>Dernier brouillon reçu pour une unité alliée contrôlée par un autre joueur.</summary>
        bool TryGetAllyDraft(int unitId, out IReadOnlyList<UnitOrder> orders);
    }
}
