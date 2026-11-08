using System;
using System.Collections.Generic;
using BigProject.Core.Grid;

namespace BigProject.Core.Units
{
    /// <summary>
    /// Données pures d'une unité (position, PV, mana...). Aucun lien avec un GameObject :
    /// la vue lit cet état, elle ne le possède pas.
    /// </summary>
    public sealed class UnitState
    {
        private readonly List<string> spellIds;

        public UnitState(
            int id,
            int playerId,
            int teamId,
            string name,
            GridPosition position,
            int maxHealth,
            int maxMana,
            int movementPoints,
            IEnumerable<string> spellIds)
        {
            Id = id;
            PlayerId = playerId;
            TeamId = teamId;
            Name = name;
            Position = position;
            MaxHealth = maxHealth;
            Health = maxHealth;
            MaxMana = maxMana;
            Mana = maxMana;
            MovementPoints = movementPoints;
            this.spellIds = new List<string>(spellIds ?? Array.Empty<string>());
        }

        private UnitState(UnitState other)
        {
            Id = other.Id;
            PlayerId = other.PlayerId;
            TeamId = other.TeamId;
            Name = other.Name;
            Position = other.Position;
            MaxHealth = other.MaxHealth;
            Health = other.Health;
            MaxMana = other.MaxMana;
            Mana = other.Mana;
            MovementPoints = other.MovementPoints;
            spellIds = new List<string>(other.spellIds);
        }

        public int Id { get; }
        public int PlayerId { get; }
        public int TeamId { get; }
        public string Name { get; }
        public int MaxHealth { get; }
        public int MaxMana { get; }
        /// <summary>PM disponibles à chaque tour.</summary>
        public int MovementPoints { get; }
        public IReadOnlyList<string> SpellIds => spellIds;

        public GridPosition Position { get; private set; }
        public int Health { get; private set; }
        public int Mana { get; private set; }

        public bool IsAlive => Health > 0;

        public bool KnowsSpell(string spellId)
        {
            return spellIds.Contains(spellId);
        }

        public void MoveTo(GridPosition position)
        {
            Position = position;
        }

        /// <summary>Remplace l'état variable par celui reçu du serveur. Réservé à la synchronisation réseau.</summary>
        internal void ApplyStatus(GridPosition position, int health, int mana)
        {
            Position = position;
            Health = Math.Max(0, Math.Min(MaxHealth, health));
            Mana = Math.Max(0, Math.Min(MaxMana, mana));
        }

        /// <summary>Applique une variation de PV (négative = dégâts), bornée entre 0 et MaxHealth.</summary>
        public void ChangeHealth(int delta)
        {
            Health = Math.Max(0, Math.Min(MaxHealth, Health + delta));
        }

        public void SpendMana(int amount)
        {
            if (amount < 0 || amount > Mana)
            {
                throw new InvalidOperationException("Mana insuffisant pour l'unité " + Id + ".");
            }

            Mana -= amount;
        }

        public void RestoreMana(int amount)
        {
            Mana = Math.Max(0, Math.Min(MaxMana, Mana + amount));
        }

        public UnitState Clone()
        {
            return new UnitState(this);
        }
    }
}
