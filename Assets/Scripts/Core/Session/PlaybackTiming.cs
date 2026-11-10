using BigProject.Core.Resolution;

namespace BigProject.Core.Session
{
    /// <summary>
    /// Temps accordé aux clients pour rejouer l'animation d'un tour avant que le chrono de planification
    /// ne commence à compter. Serveur et clients utilisent la même formule : le chrono affiché est identique.
    /// </summary>
    public static class PlaybackTiming
    {
        /// <summary>Marge au démarrage du match (chargement de la scène de combat).</summary>
        public const float InitialAllowanceSeconds = 3f;

        private const float SecondsPerBeat = 0.8f;
        private const float PaddingSeconds = 0.5f;

        public static float Estimate(RoundResult result)
        {
            return result.Beats.Count * SecondsPerBeat + PaddingSeconds;
        }
    }
}
