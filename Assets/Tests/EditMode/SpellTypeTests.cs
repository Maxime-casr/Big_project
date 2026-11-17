using System;
using System.Collections.Generic;
using BigProject.Core.Characters;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Net;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using NUnit.Framework;

namespace BigProject.Tests
{
    public class SpellTypeTests
    {
        private SpellCatalog spells;
        private RoundResolver resolver;

        [SetUp]
        public void SetUp()
        {
            spells = SpellCatalog.CreateDefault();
            resolver = new RoundResolver(spells, new LastTeamStandingCondition(), new Random(11));
        }

        private static GridPosition P(int x, int y)
        {
            return new GridPosition(x, y);
        }

        private MatchState NewState()
        {
            return new MatchState(new GridMap(12, 12), new MatchConfig { ManaRegenPerRound = 0 });
        }

        private UnitState AddUnit(MatchState state, int id, int team, GridPosition position, int health = 30)
        {
            var unit = new UnitState(id, id, team, "U" + id, position, health, 12, 5, spells.AllIds);
            state.AddUnit(unit);
            return unit;
        }

        private static TurnPlan Plan(int playerId, int unitId, params UnitOrder[] orders)
        {
            return new TurnPlan(playerId, new[] { new UnitPlan(unitId, orders) });
        }

        private static MoveOrder Move(params GridPosition[] path)
        {
            return new MoveOrder(path);
        }

        private static CastOrder Bolt(UnitState target)
        {
            return new CastOrder("seeking_bolt", target.Position, target.Id);
        }

        // --- Sort sur une unité ---

        [Test]
        public void UnitSpell_FollowsTheTargetEvenIfItMovedInTheSameAction()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(4, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Bolt(enemy)),
                Plan(2, 2, Move(P(4, 2)))
            });

            Assert.AreEqual(P(4, 2), enemy.Position);
            Assert.AreEqual(25, enemy.Health, "Le sort ciblé touche l'unité où qu'elle soit allée.");
        }

        [Test]
        public void UnitSpell_CastIsRefusedWhenTheTargetStartedTheActionOutOfRange()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(5, 2));

            // Action 1 : l'ennemi s'éloigne hors de portée (portée 4) ; action 2 : le sort part trop tard.
            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(1, 2)), Bolt(enemy)),
                Plan(2, 2, Move(P(6, 2)))
            });

            Assert.AreEqual(30, enemy.Health);
            Assert.AreEqual(12, caster.Mana, "Un sort perdu ne coûte pas de mana.");
        }

        [Test]
        public void UnitSpell_RangeIsJudgedWhenTheActionStarts_NotAfterTheTargetMoves()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(5, 1));

            // Le sort est lancé depuis (1,1) vers (5,1) : à portée 4 au début de l'action. L'ennemi s'éloigne
            // pendant la même action : le sort est déjà parti et le touche quand même.
            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Bolt(enemy)),
                Plan(2, 2, Move(P(6, 1)))
            });

            Assert.AreEqual(25, enemy.Health);
        }

        [Test]
        public void UnitSpell_PlanWithATargetOutOfRangeAlreadyAtPlanning_IsRejected()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(9, 9));

            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("seeking_bolt", enemy.Position, enemy.Id), Move(P(2, 1)))
            });

            Assert.AreEqual(1, result.RejectedPlans.Count);
            Assert.AreEqual(P(1, 1), caster.Position, "Un plan invalide est refusé en entier.");
        }

        [Test]
        public void UnitSpell_TargetThatLeftRangeBeforeLaunch_CancelsTheNextAction()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(5, 2));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(1, 2)), Bolt(enemy), Move(P(1, 3))),
                Plan(2, 2, Move(P(6, 2)))
            });

            Assert.AreEqual(P(1, 2), caster.Position, "Le sort perdu interrompt la marche qui suit.");
        }

        [Test]
        public void UnitSpell_ValidationRejectsATargetOfTheWrongSide()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState ally = AddUnit(state, 2, 0, P(2, 1));

            PlanProjection projection = PlanValidator.Project(
                state, spells, caster, new UnitOrder[] { new CastOrder("seeking_bolt", ally.Position, ally.Id) });

            Assert.IsFalse(projection.IsValid);
        }

        [Test]
        public void UnitSpell_ValidationRejectsADeadOrUnknownTarget()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(3, 1));
            enemy.ChangeHealth(-100);

            Assert.IsFalse(PlanValidator.Project(state, spells, caster, new UnitOrder[] { Bolt(enemy) }).IsValid);
            Assert.IsFalse(PlanValidator.Project(
                state, spells, caster, new UnitOrder[] { new CastOrder("seeking_bolt", P(3, 1), 999) }).IsValid);
        }

        [Test]
        public void SelfSpell_NeedsNoTargetAndAppliesToTheCaster()
        {
            var catalog = new SpellCatalog();
            catalog.Add(new SpellDefinition(
                "rest", "Repos", 1, 0, 0, false, false, AreaShape.Single, 0,
                new[] { SpellEffect.Heal(4, TargetFilter.Self) }, TargetKind.Self));
            var self = new RoundResolver(catalog, new LastTeamStandingCondition(), new Random(1));

            MatchState state = NewState();
            var caster = new UnitState(1, 1, 0, "A", P(1, 1), 30, 8, 4, catalog.AllIds);
            state.AddUnit(caster);
            state.AddUnit(new UnitState(2, 2, 1, "B", P(9, 9), 30, 8, 4, catalog.AllIds));
            caster.ChangeHealth(-10);

            self.Resolve(state, new[] { Plan(1, 1, new CastOrder("rest", caster.Position, caster.Id)) });

            Assert.AreEqual(24, caster.Health);
        }

        // --- Téléportation ---

        [Test]
        public void Teleport_MovesTheCasterWithoutCrossingTheCellsBetween()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(11, 11));
            state.Map.SetTile(P(2, 1), TileType.Obstacle);
            state.Map.SetTile(P(3, 1), TileType.Obstacle);

            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("blink", P(4, 1)))
            });

            Assert.AreEqual(P(4, 1), caster.Position);
            Assert.IsTrue(result.Beats[0].Events.Exists(e => e is UnitTeleportedEvent));
        }

        [Test]
        public void Teleport_ProjectionMovesTheStartOfTheNextAction()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));

            PlanProjection projection = PlanValidator.Project(
                state, spells, caster, new UnitOrder[] { new CastOrder("blink", P(4, 1)), Move(P(5, 1)) });

            Assert.IsTrue(projection.IsValid);
            Assert.AreEqual(P(5, 1), projection.FinalPosition);
        }

        [Test]
        public void Teleport_ThenWalkInTheSameRound_StartsFromTheNewCell()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(11, 11));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("blink", P(4, 1)), Move(P(5, 1)), Move(P(6, 1)))
            });

            Assert.AreEqual(P(6, 1), caster.Position);
        }

        [Test]
        public void Teleport_ToACellAnotherUnitIsStandingOn_FailsAndInterrupts()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(4, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("blink", P(4, 1)), Move(P(1, 2)))
            });

            Assert.AreEqual(P(1, 1), caster.Position, "La téléportation échoue et la marche suivante est annulée.");
        }

        [Test]
        public void Teleport_AndWalkerAimingAtTheSameCell_OneWinsAtRandom()
        {
            int teleporterWins = 0;
            int walkerWins = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                MatchState state = NewState();
                UnitState teleporter = AddUnit(state, 1, 0, P(1, 1));
                UnitState walker = AddUnit(state, 2, 1, P(5, 1));
                var seeded = new RoundResolver(spells, new LastTeamStandingCondition(), new Random(seed));

                seeded.Resolve(state, new[]
                {
                    Plan(1, 1, new CastOrder("blink", P(4, 1))),
                    Plan(2, 2, Move(P(4, 1)))
                });

                bool tele = teleporter.Position == P(4, 1);
                bool walk = walker.Position == P(4, 1);
                Assert.IsTrue(tele ^ walk, "Exactement une unité obtient la case.");
                if (tele)
                {
                    teleporterWins++;
                }
                else
                {
                    walkerWins++;
                }
            }

            Assert.Greater(teleporterWins, 0);
            Assert.Greater(walkerWins, 0);
        }

        // --- Invocation ---

        [Test]
        public void Summon_CreatesAnAlliedUnitOwnedByTheCasterPlayer()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(11, 11));

            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("totem", P(3, 1)))
            });

            UnitState totem = state.GetUnit(3);
            Assert.IsNotNull(totem);
            Assert.IsTrue(totem.IsSummon);
            Assert.AreEqual(caster.Id, totem.OwnerUnitId);
            Assert.AreEqual(caster.PlayerId, totem.PlayerId);
            Assert.AreEqual(caster.TeamId, totem.TeamId);
            Assert.AreEqual(P(3, 1), totem.Position);
            Assert.AreEqual(12, totem.Health);
            Assert.IsTrue(result.Beats[0].Events.Exists(e => e is UnitSummonedEvent));
        }

        [Test]
        public void Summon_BlocksAWalkerThatArrivesAfterIt()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState walker = AddUnit(state, 2, 1, P(6, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("totem", P(3, 1))),
                Plan(2, 2, Move(P(5, 1)), Move(P(4, 1)), Move(P(3, 1)), Move(P(2, 1)))
            });

            Assert.AreEqual(P(4, 1), walker.Position, "L'invocation est apparue avant : le marcheur s'arrête devant.");
            Assert.IsNotNull(state.GetUnit(3));
        }

        [Test]
        public void Summon_AndWalkerAimingAtTheSameCellOnTheFirstStep_OneWinsAtRandom()
        {
            int summonWins = 0;
            int walkerWins = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                MatchState state = NewState();
                AddUnit(state, 1, 0, P(1, 1));
                UnitState walker = AddUnit(state, 2, 1, P(4, 1));
                var seeded = new RoundResolver(spells, new LastTeamStandingCondition(), new Random(seed));

                seeded.Resolve(state, new[]
                {
                    Plan(1, 1, new CastOrder("totem", P(3, 1))),
                    Plan(2, 2, Move(P(3, 1)))
                });

                bool summoned = state.GetUnit(3) != null;
                bool walked = walker.Position == P(3, 1);
                Assert.IsTrue(summoned ^ walked, "Exactement un des deux obtient la case.");
                if (summoned)
                {
                    summonWins++;
                }
                else
                {
                    walkerWins++;
                }
            }

            Assert.Greater(summonWins, 0);
            Assert.Greater(walkerWins, 0);
        }

        [Test]
        public void Summon_OnAnOccupiedCell_FailsAndInterruptsTheCaster()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(3, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("totem", P(3, 1)), Move(P(1, 2)))
            });

            Assert.IsNull(state.GetUnit(3));
            Assert.AreEqual(P(1, 1), caster.Position);
        }

        [Test]
        public void Summon_CountsAsAPlayerUnit_ItIsPlannedAndResolvedNextRound()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(3, 4));

            resolver.Resolve(state, new[] { Plan(1, 1, new CastOrder("totem", P(3, 1))) });
            UnitState totem = state.GetUnit(3);
            Assert.IsNotNull(totem);

            resolver.Resolve(state, new[]
            {
                Plan(1, 3, new CastOrder("piercing_arrow", enemy.Position))
            });

            Assert.AreEqual(26, enemy.Health);
        }

        [Test]
        public void Summon_IsLimitedPerPlayer()
        {
            MatchState state = NewState();
            state.Config.MaxSummonsPerPlayer = 1;
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(11, 11));
            caster.RestoreMana(20);

            resolver.Resolve(state, new[] { Plan(1, 1, new CastOrder("totem", P(3, 1))) });
            Assert.IsNotNull(state.GetUnit(3));

            PlanProjection second = PlanValidator.Project(
                state, spells, caster, new UnitOrder[] { new CastOrder("totem", P(3, 2)) });

            Assert.IsFalse(second.IsValid);
        }

        [Test]
        public void Summon_ProjectionListsTheCellsForTheGhosts()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));

            PlanProjection projection = PlanValidator.Project(
                state, spells, caster, new UnitOrder[] { new CastOrder("totem", P(3, 1)) });

            Assert.IsTrue(projection.IsValid);
            CollectionAssert.AreEqual(new[] { P(3, 1) }, projection.SummonCells);
            CollectionAssert.AreEqual(new[] { "totem" }, projection.SummonCharacterIds);
        }

        [Test]
        public void Summons_DoNotCountForVictory()
        {
            MatchState state = NewState();
            UnitState hero = AddUnit(state, 1, 0, P(1, 1), health: 5);
            AddUnit(state, 2, 1, P(3, 1));
            state.AddUnit(new UnitState(3, 1, 0, "Totem", P(2, 5), 12, 4, 0, new string[0], true, 1));

            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(2, 2, new CastOrder("fireball", P(1, 1)))
            });

            Assert.IsFalse(hero.IsAlive);
            Assert.IsTrue(result.MatchEnded, "L'équipe n'a plus que son invocation : elle a perdu.");
            Assert.AreEqual(1, result.WinnerTeamId);
        }

        // --- Contenu et réseau ---

        [Test]
        public void ContentHash_IsStableAndChangesWithTheRules()
        {
            int first = SpellCatalog.CreateDefault().ComputeHash();
            Assert.AreEqual(first, SpellCatalog.CreateDefault().ComputeHash());

            var altered = new SpellCatalog();
            altered.Add(new SpellDefinition(
                "fireball", "Boule de feu", 3, 2, 5, true, false, AreaShape.Single, 0,
                new[] { SpellEffect.Damage(7, TargetFilter.Enemies) }));

            Assert.AreNotEqual(first, altered.ComputeHash());
        }

        [Test]
        public void Catalog_RejectsATeleportThatTargetsAUnit()
        {
            var catalog = new SpellCatalog();

            Assert.Throws<InvalidOperationException>(() => catalog.Add(new SpellDefinition(
                "bad", "Mauvais", 1, 0, 3, false, false, AreaShape.Single, 0,
                new[] { SpellEffect.Teleport() }, TargetKind.Unit)));
        }

        [Test]
        public void Content_ReportsASummonOfAnUnknownCharacter()
        {
            var catalog = new SpellCatalog();
            catalog.Add(new SpellDefinition(
                "call", "Appel", 1, 0, 3, false, false, AreaShape.Single, 0,
                new[] { SpellEffect.SummonCharacter("inexistant") }));
            var characters = new CharacterCatalog();
            characters.Add(new CharacterDefinition("hero", "Héros", 10, 5, 3, new[] { "call" }));

            List<string> errors = ContentRules.FindErrors(catalog, characters);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("inexistant", errors[0]);
        }

        [Test]
        public void Network_ServerRefusesAClientWithDifferentSpells()
        {
            var net = new LoopbackNetwork();
            var server = new GameServer(
                net, new ServerSettings { PlayersPerTeam = 1 }, SpellCatalog.CreateDefault(), new Random(1));

            var differing = new SpellCatalog();
            differing.Add(new SpellDefinition(
                "fireball", "Boule de feu", 3, 2, 5, true, false, AreaShape.Single, 0,
                new[] { SpellEffect.Damage(99, TargetFilter.Enemies) }));
            LoopbackClient transport = net.CreateClient();
            var session = new NetworkMatchSession(transport, differing, "Triche");
            string failure = null;
            session.Failed += text => failure = text;

            transport.Connect();

            Assert.Contains(transport.Id, net.DisconnectedByServer);
            Assert.IsNotNull(failure);
            Assert.AreEqual(0, server.QueuedPlayerCount);
        }

        [Test]
        public void Network_NewEventsAndTargetUnitSurviveEncoding()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(3, 1));
            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("totem", P(1, 3)), new CastOrder("blink", P(4, 1))),
                Plan(2, 2, Move(P(3, 2)), new CastOrder("seeking_bolt", P(1, 1), 1))
            });

            var decoded = (RoundResolvedMessage)MessageCodec.Decode(
                MessageCodec.Encode(new RoundResolvedMessage(result, 20f, MatchSnapshot.From(state))));

            var kinds = new HashSet<Type>();
            foreach (ResolutionBeat beat in decoded.Result.Beats)
            {
                foreach (GameEvent gameEvent in beat.Events)
                {
                    kinds.Add(gameEvent.GetType());
                }
            }

            Assert.Contains(typeof(UnitSummonedEvent), new List<Type>(kinds));
            Assert.Contains(typeof(UnitTeleportedEvent), new List<Type>(kinds));
            Assert.Contains(typeof(SpellImpactEvent), new List<Type>(kinds));

            var submit = (SubmitPlanMessage)MessageCodec.Decode(MessageCodec.Encode(new SubmitPlanMessage(
                new[] { new UnitPlan(2, new UnitOrder[] { new CastOrder("seeking_bolt", P(1, 1), 1) }) })));
            var order = (CastOrder)submit.Units[0].Orders[0];
            Assert.AreEqual(1, order.TargetUnitId);
            Assert.IsNotNull(enemy);
        }

        [Test]
        public void Network_ClientsReceiveSummonedUnitsThroughTheSnapshot()
        {
            var net = new LoopbackNetwork();
            var server = new GameServer(
                net, new ServerSettings { PlayersPerTeam = 1, PlanningSeconds = 10f }, spells, new Random(2));

            var sessions = new List<NetworkMatchSession>();
            for (int i = 0; i < 2; i++)
            {
                LoopbackClient transport = net.CreateClient();
                sessions.Add(new NetworkMatchSession(transport, spells, "P" + i, "archer"));
                transport.Connect();
            }

            NetworkMatchSession first = sessions[0];
            UnitState own = null;
            foreach (UnitState unit in first.State.Units)
            {
                if (unit.PlayerId == first.LocalPlayerId)
                {
                    own = unit;
                }
            }

            GridPosition target = new GridPosition(own.Position.X + (own.Position.X < 6 ? 2 : -2), own.Position.Y);
            first.TrySubmitPlan(
                new TurnPlan(first.LocalPlayerId, new[] { new UnitPlan(own.Id, new UnitOrder[] { new CastOrder("totem", target) }) }),
                out _);
            sessions[1].TrySubmitPlan(new TurnPlan(sessions[1].LocalPlayerId, new UnitPlan[0]), out _);

            foreach (NetworkMatchSession session in sessions)
            {
                UnitState summoned = session.State.GetUnit(3);
                Assert.IsNotNull(summoned, "Chaque client reçoit l'invocation dans l'état du match.");
                Assert.IsTrue(summoned.IsSummon);
                Assert.AreEqual(own.PlayerId, summoned.PlayerId);
                Assert.AreEqual(target, summoned.Position);
            }

            Assert.IsNotNull(server);
        }
    }
}
