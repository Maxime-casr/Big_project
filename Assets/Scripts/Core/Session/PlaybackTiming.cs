using System.Collections.Generic;
using BigProject.Core.Resolution;

namespace BigProject.Core.Session
{
    /// <summary>
    /// Durée de lecture d'un tour : chaque action dure <see cref="ActionSeconds"/>, suivie d'une pause de
    /// <see cref="PauseSeconds"/> avant la suivante. Sert aux clients pour rejouer, et au chrono de planification
    /// qui ne commence à compter qu'après la lecture : serveur et clients utilisent la même formule,
    /// le chrono affiché est identique.
    /// </summary>
    public static class PlaybackTiming
    {
        /// <summary>Marge au démarrage du match (chargement de la scène de combat).</summary>
        public const float InitialAllowanceSeconds = 3f;

        /// <summary>Durée d'une action (tous les déplacements, lancers et impacts qu'elle contient).</summary>
        public const float ActionSeconds = 0.8f;

        /// <summary>Pause entre deux actions.</summary>
        public const float PauseSeconds = 0.2f;

        private const float PaddingSeconds = 0.5f;

        public static float Estimate(RoundResult result)
        {
            int actions = CountActions(result);
            if (actions == 0)
            {
                return PaddingSeconds;
            }

            return actions * ActionSeconds + (actions - 1) * PauseSeconds + PaddingSeconds;
        }

        /// <summary>Le nombre d'actions rejouées : une action sans le moindre événement ne prend pas de temps.</summary>
        public static int CountActions(RoundResult result)
        {
            var steps = new HashSet<int>();
            foreach (ResolutionBeat beat in result.Beats)
            {
                steps.Add(beat.StepIndex);
            }

            return steps.Count;
        }
    }
}
