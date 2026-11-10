using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Units;

namespace BigProject.Core.Net
{
    public sealed class UnitSnapshot
    {
        public int Id;
        public int PlayerId;
        public int TeamId;
        public string Name;
        public int X;
        public int Y;
        public int Health;
        public int MaxHealth;
        public int Mana;
        public int MaxMana;
        public int MovementPoints;
        public bool IsSummon;
        public int OwnerUnitId = UnitState.NoOwner;
        public List<string> SpellIds = new List<string>();
    }

    /// <summary>
    /// Copie sérialisable de l'état d'un match. Le serveur l'envoie au début puis après chaque tour :
    /// les clients n'ont jamais à recalculer les règles pour rester synchronisés.
    /// </summary>
    public sealed class MatchSnapshot
    {
        public int Width;
        public int Height;
        public int OriginX;
        public int OriginY;
        public TileType[] Tiles;
        public int RoundNumber;
        public MatchPhase Phase;
        public int WinnerTeamId;
        public int ManaRegenPerRound;
        public int MaxOrdersPerUnit;
        public int MaxSummonsPerPlayer;
        public List<UnitSnapshot> Units = new List<UnitSnapshot>();

        public static MatchSnapshot From(MatchState state)
        {
            var snapshot = new MatchSnapshot
            {
                Width = state.Map.Width,
                Height = state.Map.Height,
                OriginX = state.Map.OriginX,
                OriginY = state.Map.OriginY,
                Tiles = new TileType[state.Map.Width * state.Map.Height],
                RoundNumber = state.RoundNumber,
                Phase = state.Phase,
                WinnerTeamId = state.WinnerTeamId,
                ManaRegenPerRound = state.Config.ManaRegenPerRound,
                MaxOrdersPerUnit = state.Config.MaxOrdersPerUnit,
                MaxSummonsPerPlayer = state.Config.MaxSummonsPerPlayer
            };

            for (int y = 0; y < state.Map.Height; y++)
            {
                for (int x = 0; x < state.Map.Width; x++)
                {
                    snapshot.Tiles[y * state.Map.Width + x] = state.Map.GetTile(new GridPosition(x, y));
                }
            }

            foreach (UnitState unit in state.Units)
            {
                snapshot.Units.Add(new UnitSnapshot
                {
                    Id = unit.Id,
                    PlayerId = unit.PlayerId,
                    TeamId = unit.TeamId,
                    Name = unit.Name,
                    X = unit.Position.X,
                    Y = unit.Position.Y,
                    Health = unit.Health,
                    MaxHealth = unit.MaxHealth,
                    Mana = unit.Mana,
                    MaxMana = unit.MaxMana,
                    MovementPoints = unit.MovementPoints,
                    IsSummon = unit.IsSummon,
                    OwnerUnitId = unit.OwnerUnitId,
                    SpellIds = new List<string>(unit.SpellIds)
                });
            }

            return snapshot;
        }

        /// <summary>Construit un état complet (début de match côté client).</summary>
        public MatchState CreateState()
        {
            var map = new GridMap(Width, Height) { OriginX = OriginX, OriginY = OriginY };
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    map.SetTile(new GridPosition(x, y), Tiles[y * Width + x]);
                }
            }

            var state = new MatchState(map, new MatchConfig
            {
                ManaRegenPerRound = ManaRegenPerRound,
                MaxOrdersPerUnit = MaxOrdersPerUnit,
                MaxSummonsPerPlayer = MaxSummonsPerPlayer
            });

            try
            {
                foreach (UnitSnapshot data in Units)
                {
                    state.AddUnit(CreateUnit(data));
                }
            }
            catch (System.InvalidOperationException exception)
            {
                throw new ProtocolException("Instantané incohérent : " + exception.Message);
            }

            state.RoundNumber = RoundNumber;
            state.Phase = Phase;
            state.WinnerTeamId = WinnerTeamId;
            return state;
        }

        private static UnitState CreateUnit(UnitSnapshot data)
        {
            var position = new GridPosition(data.X, data.Y);
            var unit = new UnitState(
                data.Id, data.PlayerId, data.TeamId, data.Name, position,
                data.MaxHealth, data.MaxMana, data.MovementPoints, data.SpellIds,
                data.IsSummon, data.OwnerUnitId);
            unit.ApplyStatus(position, data.Health, data.Mana);
            return unit;
        }

        /// <summary>
        /// Aligne un état existant sur l'instantané du serveur (fin de tour). Les unités apparues pendant le
        /// tour (invocations) sont créées.
        /// </summary>
        public void ApplyTo(MatchState state)
        {
            // D'abord les unités connues : elles libèrent les cases que les nouvelles vont occuper.
            foreach (UnitSnapshot data in Units)
            {
                UnitState unit = state.GetUnit(data.Id);
                if (unit == null)
                {
                    continue;
                }

                var position = new GridPosition(data.X, data.Y);
                if (!state.Map.IsInside(position))
                {
                    throw new ProtocolException("Position hors carte dans l'instantané.");
                }

                unit.ApplyStatus(position, data.Health, data.Mana);
            }

            foreach (UnitSnapshot data in Units)
            {
                if (state.GetUnit(data.Id) != null)
                {
                    continue;
                }

                try
                {
                    state.AddUnit(CreateUnit(data));
                }
                catch (System.InvalidOperationException exception)
                {
                    throw new ProtocolException("Instantané incohérent : " + exception.Message);
                }
            }

            state.RoundNumber = RoundNumber;
            state.Phase = Phase;
            state.WinnerTeamId = WinnerTeamId;
        }

        internal void Write(NetWriter writer)
        {
            writer.WriteInt(Width);
            writer.WriteInt(Height);
            writer.WriteInt(OriginX);
            writer.WriteInt(OriginY);
            foreach (TileType tile in Tiles)
            {
                writer.WriteByte((byte)tile);
            }

            writer.WriteInt(RoundNumber);
            writer.WriteByte((byte)Phase);
            writer.WriteInt(WinnerTeamId);
            writer.WriteInt(ManaRegenPerRound);
            writer.WriteInt(MaxOrdersPerUnit);
            writer.WriteInt(MaxSummonsPerPlayer);

            writer.WriteInt(Units.Count);
            foreach (UnitSnapshot unit in Units)
            {
                writer.WriteInt(unit.Id);
                writer.WriteInt(unit.PlayerId);
                writer.WriteInt(unit.TeamId);
                writer.WriteString(unit.Name);
                writer.WriteInt(unit.X);
                writer.WriteInt(unit.Y);
                writer.WriteInt(unit.Health);
                writer.WriteInt(unit.MaxHealth);
                writer.WriteInt(unit.Mana);
                writer.WriteInt(unit.MaxMana);
                writer.WriteInt(unit.MovementPoints);
                writer.WriteBool(unit.IsSummon);
                writer.WriteInt(unit.OwnerUnitId);
                writer.WriteInt(unit.SpellIds.Count);
                foreach (string spellId in unit.SpellIds)
                {
                    writer.WriteString(spellId);
                }
            }
        }

        internal static MatchSnapshot Read(NetReader reader)
        {
            var snapshot = new MatchSnapshot
            {
                Width = reader.ReadCount(NetLimits.MaxMapSide),
                Height = reader.ReadCount(NetLimits.MaxMapSide)
            };

            if (snapshot.Width == 0 || snapshot.Height == 0)
            {
                throw new ProtocolException("Carte vide.");
            }

            snapshot.OriginX = reader.ReadInt();
            snapshot.OriginY = reader.ReadInt();
            if (Math.Abs(snapshot.OriginX) > NetLimits.MaxOrigin || Math.Abs(snapshot.OriginY) > NetLimits.MaxOrigin)
            {
                throw new ProtocolException("Origine de carte hors limites.");
            }

            snapshot.Tiles = new TileType[snapshot.Width * snapshot.Height];
            for (int i = 0; i < snapshot.Tiles.Length; i++)
            {
                snapshot.Tiles[i] = reader.ReadEnum<TileType>();
            }

            snapshot.RoundNumber = reader.ReadInt();
            snapshot.Phase = reader.ReadEnum<MatchPhase>();
            snapshot.WinnerTeamId = reader.ReadInt();
            snapshot.ManaRegenPerRound = reader.ReadInt();
            snapshot.MaxOrdersPerUnit = reader.ReadCount(NetLimits.MaxOrdersPerUnit);
            snapshot.MaxSummonsPerPlayer = reader.ReadCount(NetLimits.MaxUnits);

            int unitCount = reader.ReadCount(NetLimits.MaxUnits);
            for (int i = 0; i < unitCount; i++)
            {
                var unit = new UnitSnapshot
                {
                    Id = reader.ReadInt(),
                    PlayerId = reader.ReadInt(),
                    TeamId = reader.ReadInt(),
                    Name = reader.ReadString(),
                    X = reader.ReadInt(),
                    Y = reader.ReadInt(),
                    Health = reader.ReadInt(),
                    MaxHealth = reader.ReadInt(),
                    Mana = reader.ReadInt(),
                    MaxMana = reader.ReadInt(),
                    MovementPoints = reader.ReadInt(),
                    IsSummon = reader.ReadBool(),
                    OwnerUnitId = reader.ReadInt()
                };

                int spellCount = reader.ReadCount(NetLimits.MaxSpellsPerUnit);
                for (int s = 0; s < spellCount; s++)
                {
                    unit.SpellIds.Add(reader.ReadString());
                }

                snapshot.Units.Add(unit);
            }

            return snapshot;
        }
    }
}
