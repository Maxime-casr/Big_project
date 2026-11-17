using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using NUnit.Framework;

namespace BigProject.Tests
{
    public class RoundResolverTests
    {
        private SpellCatalog spells;
        private RoundResolver resolver;

        [SetUp]
        public void SetUp()
        {
            spells = SpellCatalog.CreateDefault();
            resolver = new RoundResolver(spells, new LastTeamStandingCondition(), new Random(7));
        }

        private static GridPosition P(int x, int y)
        {
            return new GridPosition(x, y);
        }

        private MatchState NewState(int width = 9, int height = 9)
        {
            return new MatchState(new GridMap(width, height), new MatchConfig { ManaRegenPerRound = 0 });
        }

        private UnitState AddUnit(MatchState state, int id, int team, GridPosition position, int health = 30)
        {
            var unit = new UnitState(id, id, team, "U" + id, position, health, 8, 4, spells.AllIds);
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

        [Test]
        public void SingleMove_MovesUnitAlongPath()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(8, 8));

            RoundResult result = resolver.Resolve(state, new[] { Plan(1, 1, Move(P(2, 1)), Move(P(3, 1))) });

            Assert.AreEqual(P(3, 1), a.Position);
            Assert.AreEqual(2, result.Beats.Count, "Une case par action : deux cases, deux actions.");
            Assert.AreEqual(2, state.RoundNumber);
        }

        [Test]
        public void TwoUnitsTargetingSameCell_ExactlyOneGetsThrough()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(1, 1));
            UnitState b = AddUnit(state, 2, 1, P(3, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(2, 1))),
                Plan(2, 2, Move(P(2, 1)))
            });

            bool aMoved = a.Position == P(2, 1);
            bool bMoved = b.Position == P(2, 1);
            Assert.IsTrue(aMoved ^ bMoved, "Une seule unité doit obtenir la case.");
            Assert.AreEqual(aMoved ? P(3, 1) : P(1, 1), aMoved ? b.Position : a.Position, "L'autre reste sur place.");
        }

        [Test]
        public void ContestedCell_PriorityIsRandomButBothOutcomesHappen()
        {
            int aWins = 0;
            int bWins = 0;
            for (int seed = 0; seed < 30; seed++)
            {
                MatchState state = NewState();
                UnitState a = AddUnit(state, 1, 0, P(1, 1));
                AddUnit(state, 2, 1, P(3, 1));
                var seeded = new RoundResolver(spells, new LastTeamStandingCondition(), new Random(seed));

                seeded.Resolve(state, new[] { Plan(1, 1, Move(P(2, 1))), Plan(2, 2, Move(P(2, 1))) });

                if (a.Position == P(2, 1))
                {
                    aWins++;
                }
                else
                {
                    bWins++;
                }
            }

            Assert.Greater(aWins, 0);
            Assert.Greater(bWins, 0);
        }

        [Test]
        public void UnitThatLosesTheContest_LosesItsFollowingActions()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(1, 1));
            UnitState b = AddUnit(state, 2, 1, P(3, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(2, 1)), new CastOrder("piercing_arrow", P(2, 5))),
                Plan(2, 2, Move(P(2, 1)), new CastOrder("piercing_arrow", P(2, 5)))
            });

            int spent = (8 - a.Mana) + (8 - b.Mana);
            Assert.AreEqual(2, spent, "Seul le gagnant lance son sort : le perdant est interrompu.");
        }

        [Test]
        public void BlockedUnit_CancelsItsNextSpell()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(2, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(2, 1)), new CastOrder("piercing_arrow", P(2, 1)))
            });

            Assert.AreEqual(P(1, 1), a.Position);
            Assert.AreEqual(30, enemy.Health);
            Assert.AreEqual(8, a.Mana, "Un sort annulé ne coûte pas de mana.");
        }

        [Test]
        public void AllyBlockedByAnInterruptedAlly_IsInterruptedToo()
        {
            MatchState state = NewState();
            UnitState first = AddUnit(state, 1, 0, P(1, 1));
            UnitState behind = AddUnit(state, 2, 0, P(0, 1));
            AddUnit(state, 3, 1, P(2, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(2, 1))),
                Plan(2, 2, Move(P(1, 1)), Move(P(2, 1)))
            });

            Assert.AreEqual(P(1, 1), first.Position);
            Assert.AreEqual(P(0, 1), behind.Position, "L'allié derrière ne peut pas avancer sur la case restée occupée.");
        }

        [Test]
        public void SpellIsCastAtTheStartAndImpactsAtTheEndOfTheSameAction()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            UnitState target = AddUnit(state, 2, 1, P(1, 4));
            AddUnit(state, 3, 0, P(5, 5));

            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("piercing_arrow", P(1, 4))),
                Plan(3, 3, Move(P(6, 5)))
            });

            Assert.AreEqual(1, result.Beats.Count);
            Assert.IsInstanceOf<SpellCastEvent>(result.Beats[0].Events[0]);
            Assert.IsTrue(result.Beats[0].Events.Exists(e => e is UnitMovedEvent));
            Assert.IsTrue(result.Beats[0].Events.Exists(e => e is UnitDamagedEvent));
            Assert.AreEqual(26, target.Health);
            Assert.AreEqual(0, result.Beats[0].StepIndex);
        }

        [Test]
        public void SwapPlaces_IsBlockedForBoth()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(1, 1));
            UnitState b = AddUnit(state, 2, 1, P(2, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(2, 1))),
                Plan(2, 2, Move(P(1, 1)))
            });

            Assert.AreEqual(P(1, 1), a.Position);
            Assert.AreEqual(P(2, 1), b.Position);
        }

        [Test]
        public void FollowingAUnitThatLeaves_IsAllowed()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(1, 1));
            UnitState b = AddUnit(state, 2, 0, P(2, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(2, 1))),
                Plan(2, 2, Move(P(3, 1)))
            });

            Assert.AreEqual(P(2, 1), a.Position);
            Assert.AreEqual(P(3, 1), b.Position);
        }

        [Test]
        public void MovingIntoStationaryUnit_StopsBeforeIt()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(1, 1));
            UnitState b = AddUnit(state, 2, 1, P(3, 1));

            resolver.Resolve(state, new[] { Plan(1, 1, Move(P(2, 1)), Move(P(3, 1)), Move(P(4, 1))) });

            Assert.AreEqual(P(2, 1), a.Position);
            Assert.AreEqual(P(3, 1), b.Position);
        }

        [Test]
        public void SpellHitsPredictedCell_AfterTargetMovesThere()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState target = AddUnit(state, 2, 1, P(5, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("fireball", P(4, 1))),
                Plan(2, 2, Move(P(4, 1)))
            });

            Assert.AreEqual(24, target.Health);
            Assert.AreEqual(5, caster.Mana);
        }

        [Test]
        public void SpellOnCellTargetLeft_MissesAndStillCostsMana()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState target = AddUnit(state, 2, 1, P(4, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("fireball", P(4, 1))),
                Plan(2, 2, Move(P(4, 2)))
            });

            Assert.AreEqual(30, target.Health);
            Assert.AreEqual(5, caster.Mana);
        }

        [Test]
        public void MovesAreProcessedBeforeSpellsInTheSameStep()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            UnitState target = AddUnit(state, 2, 1, P(4, 1));

            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("fireball", P(4, 1))),
                Plan(2, 2, Move(P(5, 1)))
            });

            Assert.AreEqual(30, target.Health);
            Assert.AreEqual(1, result.Beats.Count, "Un déplacement d'une case et un sort : un seul instant.");
            Assert.IsInstanceOf<SpellCastEvent>(result.Beats[0].Events[0]);
            Assert.IsTrue(result.Beats[0].Events.Exists(e => e is UnitMovedEvent));
        }

        [Test]
        public void Orders_ExecuteInOrder_MoveThenCastFromNewPosition()
        {
            MatchState state = NewState();
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            UnitState target = AddUnit(state, 2, 1, P(8, 1));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(2, 1)), Move(P(3, 1)), Move(P(4, 1)), new CastOrder("fireball", P(8, 1)))
            });

            Assert.AreEqual(P(4, 1), caster.Position);
            Assert.AreEqual(24, target.Health);
        }

        [Test]
        public void PlanExceedingMovementPoints_IsRejected()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(0, 0));
            AddUnit(state, 2, 1, P(8, 8));

            RoundResult result = resolver.Resolve(state, new[]
            {
                Plan(1, 1, Move(P(1, 0)), Move(P(2, 0)), Move(P(3, 0)), Move(P(4, 0)), Move(P(5, 0)))
            });

            Assert.AreEqual(P(0, 0), a.Position);
            Assert.AreEqual(1, result.RejectedPlans.Count);
        }

        [Test]
        public void PlanForSomeoneElsesUnit_IsRejected()
        {
            MatchState state = NewState();
            UnitState a = AddUnit(state, 1, 0, P(0, 0));
            AddUnit(state, 2, 1, P(8, 8));

            RoundResult result = resolver.Resolve(state, new[] { Plan(2, 1, Move(P(1, 0))) });

            Assert.AreEqual(P(0, 0), a.Position);
            Assert.AreEqual(1, result.RejectedPlans.Count);
        }

        [Test]
        public void ManaIsSpentThenRegeneratedAtEndOfRound()
        {
            MatchState state = NewState();
            state.Config.ManaRegenPerRound = 2;
            UnitState caster = AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(4, 1));

            resolver.Resolve(state, new[] { Plan(1, 1, new CastOrder("fireball", P(4, 1))) });

            Assert.AreEqual(8 - 3 + 2, caster.Mana);
        }

        [Test]
        public void SimultaneousDamageAndHeal_AreCombined()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            AddUnit(state, 2, 1, P(1, 5));
            UnitState victim = AddUnit(state, 3, 1, P(3, 1), health: 10);
            victim.ChangeHealth(-5);
            AddUnit(state, 4, 1, P(3, 5));

            resolver.Resolve(state, new[]
            {
                Plan(1, 1, new CastOrder("fireball", P(3, 1))),
                Plan(3, 3, new CastOrder("mend", P(3, 1)))
            });

            Assert.AreEqual(4, victim.Health);
        }

        [Test]
        public void LastTeamStanding_EndsTheMatch()
        {
            MatchState state = NewState();
            AddUnit(state, 1, 0, P(1, 1));
            UnitState enemy = AddUnit(state, 2, 1, P(4, 1), health: 5);

            RoundResult result = resolver.Resolve(state, new[] { Plan(1, 1, new CastOrder("fireball", P(4, 1))) });

            Assert.IsFalse(enemy.IsAlive);
            Assert.IsTrue(result.MatchEnded);
            Assert.AreEqual(0, result.WinnerTeamId);
            Assert.AreEqual(MatchPhase.Finished, state.Phase);
        }

        [Test]
        public void SameInputs_ProduceSameOutcome()
        {
            TurnPlan[] plans =
            {
                Plan(1, 1, Move(P(2, 1)), new CastOrder("piercing_arrow", P(2, 5))),
                Plan(2, 2, Move(P(2, 4)), Move(P(2, 5)))
            };

            MatchState first = NewState();
            AddUnit(first, 1, 0, P(1, 1));
            AddUnit(first, 2, 1, P(2, 3));
            MatchState second = first.Clone();

            new RoundResolver(spells, new LastTeamStandingCondition(), new Random(5)).Resolve(first, plans);
            new RoundResolver(spells, new LastTeamStandingCondition(), new Random(5)).Resolve(second, plans);

            for (int i = 0; i < first.Units.Count; i++)
            {
                Assert.AreEqual(first.Units[i].Position, second.Units[i].Position);
                Assert.AreEqual(first.Units[i].Health, second.Units[i].Health);
            }
        }
    }

    public class GridTests
    {
        [Test]
        public void Pathfinder_GoesAroundObstacles()
        {
            var map = new GridMap(5, 5);
            map.SetTile(new GridPosition(1, 0), TileType.Obstacle);

            List<GridPosition> path = Pathfinder.FindPath(map, new GridPosition(0, 0), new GridPosition(2, 0), 6);

            Assert.IsNotNull(path);
            Assert.AreEqual(4, path.Count);
            Assert.AreEqual(new GridPosition(2, 0), path[path.Count - 1]);
        }

        [Test]
        public void Pathfinder_RespectsMaxLength()
        {
            var map = new GridMap(5, 5);

            Assert.IsNull(Pathfinder.FindPath(map, new GridPosition(0, 0), new GridPosition(4, 0), 3));
        }

        [Test]
        public void LineOfSight_IsBlockedByObstacleButNotByPit()
        {
            var map = new GridMap(5, 1);
            map.SetTile(new GridPosition(2, 0), TileType.Pit);
            Assert.IsTrue(LineOfSight.HasLineOfSight(map, new GridPosition(0, 0), new GridPosition(4, 0)));

            map.SetTile(new GridPosition(2, 0), TileType.Obstacle);
            Assert.IsFalse(LineOfSight.HasLineOfSight(map, new GridPosition(0, 0), new GridPosition(4, 0)));
        }

        [Test]
        public void CrossArea_HasFiveCellsAtRadiusOne()
        {
            var map = new GridMap(5, 5);

            List<GridPosition> cells = SpellArea.GetCells(map, AreaShape.Cross, 1, new GridPosition(2, 2));

            Assert.AreEqual(5, cells.Count);
        }
    }
}
