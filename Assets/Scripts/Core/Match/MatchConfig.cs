namespace BigProject.Core.Match
{
    /// <summary>Réglages de règles communs à tous les joueurs d'un match.</summary>
    public sealed class MatchConfig
    {
        public int ManaRegenPerRound { get; set; } = 3;
        public int MaxOrdersPerUnit { get; set; } = 6;

        /// <summary>Invocations vivantes qu'un même joueur peut avoir en même temps.</summary>
        public int MaxSummonsPerPlayer { get; set; } = 3;
    }
}
