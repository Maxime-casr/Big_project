namespace BigProject.Core.Match
{
    /// <summary>Réglages de règles communs à tous les joueurs d'un match.</summary>
    public sealed class MatchConfig
    {
        public int ManaRegenPerRound { get; set; } = 3;
        public int MaxOrdersPerUnit { get; set; } = 6;
    }
}
