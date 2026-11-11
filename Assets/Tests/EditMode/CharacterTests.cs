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
    public class CharacterTests
    {
        private SpellCatalog spells;
        private CharacterCatalog characters;

        [SetUp]
        public void SetUp()
        {
            spells = SpellCatalog.CreateDefault();
            characters = CharacterCatalog.CreateDefault();
        }

        private static GridPosition P(int x, int y)
        {
            return new GridPosition(x, y);
        }

        private static CharacterDefinition Hero(string id = "hero", params string[] spellIds)
        {
            return new CharacterDefinition(id, "Héros", 20, 6, 3, spellIds);
        }

        // --- Catalogue ---

        [Test]
        public void DefaultContent_IsConsistent()
        {
            Assert.IsEmpty(ContentRules.FindErrors(spells, characters));
            Assert.IsEmpty(ContentRules.FindWarnings(spells, characters));
        }

        [Test]
        public void DefaultCharacters_HaveDifferentSpellsAndTotemIsNotPlayable()
        {
            CollectionAssert.AreEquivalent(new[] { "knight", "mage", "archer" }, new List<string>(characters.PlayableIds));
            Assert.IsFalse(characters.Get("totem").IsPlayable);

            CollectionAssert.AreNotEqual(characters.Get("knight").SpellIds, characters.Get("mage").SpellIds);
            CollectionAssert.AreNotEqual(characters.Get("mage").SpellIds, characters.Get("archer").SpellIds);
            Assert.Greater(characters.Get("knight").MaxHealth, characters.Get("mage").MaxHealth);
            Assert.Greater(characters.Get("mage").MaxMana, characters.Get("knight").MaxMana);
        }

        [Test]
        public void Catalog_RejectsInvalidIds()
        {
            var catalog = new CharacterCatalog();

            Assert.Throws<InvalidOperationException>(() => catalog.Add(Hero("")));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(Hero("with space")));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(Hero("accentué")));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(Hero(new string('a', 33))));
            Assert.DoesNotThrow(() => catalog.Add(Hero("ok_id-1")));
        }

        [Test]
        public void Catalog_RejectsDuplicatesAndBadStats()
        {
            var catalog = new CharacterCatalog();
            catalog.Add(Hero("a"));

            Assert.Throws<InvalidOperationException>(() => catalog.Add(Hero("a")));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(new CharacterDefinition("b", "B", 0, 5, 3, new string[0])));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(new CharacterDefinition("c", "C", 10, -1, 3, new string[0])));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(new CharacterDefinition("d", "D", 10, 5, -2, new string[0])));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(Hero("e", "fireball", "fireball")));
        }

        [Test]
        public void SpellCatalog_RejectsInvalidIdsAndMultiplePlacementEffects()
        {
            var catalog = new SpellCatalog();

            Assert.Throws<InvalidOperationException>(() => catalog.Add(new SpellDefinition(
                "bad id", "X", 1, 0, 3, false, false, AreaShape.Single, 0, new[] { SpellEffect.Damage(1, TargetFilter.Enemies) })));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(new SpellDefinition(
                "two", "X", 1, 0, 3, false, false, AreaShape.Single, 0,
                new[] { SpellEffect.Teleport(), SpellEffect.SummonCharacter("totem") })));
            Assert.Throws<InvalidOperationException>(() => catalog.Add(new SpellDefinition(
                "twice", "X", 1, 0, 3, false, false, AreaShape.Single, 0,
                new[] { SpellEffect.SummonCharacter("totem"), SpellEffect.SummonCharacter("totem") })));
        }

        [Test]
        public void ContentRules_ReportsMissingSpellsAndNoPlayableCharacter()
        {
            var onlyTotem = new CharacterCatalog();
            onlyTotem.Add(new CharacterDefinition("totem", "T", 5, 2, 0, new[] { "ghost_spell" }, isPlayable: false));

            List<string> errors = ContentRules.FindErrors(spells, onlyTotem);

            Assert.AreEqual(2, errors.Count);
            Assert.IsTrue(errors.Exists(e => e.Contains("ghost_spell")));
            Assert.IsTrue(errors.Exists(e => e.Contains("jouable")));
        }

        [Test]
        public void ContentRules_WarnsAboutACharacterWhoCanNeverCast()
        {
            var catalog = new CharacterCatalog();
            catalog.Add(new CharacterDefinition("broke", "Fauché", 20, 1, 3, new[] { "fireball" }));
            catalog.Add(new CharacterDefinition("mute", "Muet", 20, 5, 3, new string[0]));

            List<string> warnings = ContentRules.FindWarnings(spells, catalog);

            Assert.AreEqual(2, warnings.Count);
        }

        [Test]
        public void Hash_ChangesWhenACharacterOrItsSpellsChange_AndIgnoresAddOrder()
        {
            int reference = ContentRules.ComputeHash(spells, characters);
            Assert.AreEqual(reference, ContentRules.ComputeHash(SpellCatalog.CreateDefault(), CharacterCatalog.CreateDefault()));

            var changed = new CharacterCatalog();
            foreach (string id in characters.AllIds)
            {
                CharacterDefinition c = characters.Get(id);
                changed.Add(id == "mage"
                    ? new CharacterDefinition(c.Id, c.DisplayName, c.MaxHealth + 1, c.MaxMana, c.MovementPoints, c.SpellIds)
                    : c);
            }

            Assert.AreNotEqual(reference, ContentRules.ComputeHash(spells, changed));

            var reordered = new CharacterCatalog();
            var ids = new List<string>(characters.AllIds);
            ids.Reverse();
            foreach (string id in ids)
            {
                reordered.Add(characters.Get(id));
            }

            Assert.AreEqual(characters.ComputeHash(), reordered.ComputeHash());
        }

        // --- Création des unités ---

        [Test]
        public void MatchFactory_GivesEachUnitItsCharactersStatsAndSpells()
        {
            MatchState state = MatchFactory.CreateTeamMatch(
                new MatchConfig(), characters, 3, null, new[] { "knight", "mage", "archer", "archer", "knight", "mage" });

            UnitState knight = state.GetUnit(1);
            UnitState mage = state.GetUnit(2);
            UnitState archer = state.GetUnit(3);

            Assert.AreEqual("knight", knight.CharacterId);
            Assert.AreEqual(40, knight.MaxHealth);
            CollectionAssert.AreEqual(characters.Get("knight").SpellIds, knight.SpellIds);
            Assert.AreEqual(10, mage.MaxMana);
            Assert.IsTrue(mage.KnowsSpell("fireball"));
            Assert.IsFalse(mage.KnowsSpell("sword_strike"), "Un mage ne connaît pas les sorts du chevalier.");
            Assert.AreEqual(4, archer.MovementPoints);
            Assert.AreEqual("archer", state.GetUnit(4).CharacterId);
            StringAssert.Contains("Chevalier", knight.Name);
        }

        [Test]
        public void MatchFactory_CyclesThroughPlayableCharactersWhenNoneAreGiven_InBothTeams()
        {
            MatchState state = MatchFactory.CreateTeamMatch(new MatchConfig(), characters, 3);

            for (int slot = 0; slot < 3; slot++)
            {
                Assert.AreEqual(state.GetUnit(1 + slot).CharacterId, state.GetUnit(4 + slot).CharacterId);
            }

            Assert.AreEqual("knight", state.GetUnit(1).CharacterId);
            Assert.AreEqual("mage", state.GetUnit(2).CharacterId);
            Assert.AreEqual("archer", state.GetUnit(3).CharacterId);
        }

        [Test]
        public void MatchFactory_FillsMissingEntriesAndRejectsUnknownCharacters()
        {
            MatchState state = MatchFactory.CreateTeamMatch(new MatchConfig(), characters, 1, null, new[] { "", "archer" });

            Assert.AreEqual("knight", state.GetUnit(1).CharacterId);
            Assert.AreEqual("archer", state.GetUnit(2).CharacterId);
            Assert.Throws<InvalidOperationException>(
                () => MatchFactory.CreateTeamMatch(new MatchConfig(), characters, 1, null, new[] { "dragon", "mage" }));
        }

        // --- Invocations ---

        [Test]
        public void Summon_UsesTheSummonedCharactersStatsSpellsAndId()
        {
            var state = new MatchState(new GridMap(12, 12), new MatchConfig { ManaRegenPerRound = 0 });
            state.AddUnit(new UnitState(1, 1, 0, "Archer", P(1, 1), 28, 8, 4, characters.Get("archer").SpellIds, characterId: "archer"));
            state.AddUnit(new UnitState(2, 2, 1, "Autre", P(11, 11), 20, 6, 3, new string[0], characterId: "mage"));
            var resolver = new RoundResolver(spells, new LastTeamStandingCondition(), new Random(1), characters);

            resolver.Resolve(state, new[]
            {
                new TurnPlan(1, new[] { new UnitPlan(1, new UnitOrder[] { new CastOrder("totem", P(3, 1)) }) })
            });

            UnitState totem = state.GetUnit(3);
            Assert.AreEqual("totem", totem.CharacterId);
            Assert.AreEqual(12, totem.MaxHealth);
            Assert.AreEqual(0, totem.MovementPoints);
            CollectionAssert.AreEqual(new[] { "piercing_arrow" }, totem.SpellIds);
            StringAssert.Contains("Totem", totem.Name);
        }

        [Test]
        public void Summon_OfACharacterMissingFromTheCatalog_FailsWithoutCrashing()
        {
            var catalog = new SpellCatalog();
            catalog.Add(new SpellDefinition(
                "call", "Appel", 1, 0, 3, false, false, AreaShape.Single, 0,
                new[] { SpellEffect.SummonCharacter("ghost") }));
            var heroes = new CharacterCatalog();
            heroes.Add(new CharacterDefinition("hero", "Héros", 20, 6, 3, new[] { "call" }));

            var state = new MatchState(new GridMap(10, 10), new MatchConfig());
            var caster = new UnitState(1, 1, 0, "A", P(1, 1), 20, 6, 3, new[] { "call" }, characterId: "hero");
            state.AddUnit(caster);
            state.AddUnit(new UnitState(2, 2, 1, "B", P(9, 9), 20, 6, 3, new string[0]));
            var resolver = new RoundResolver(catalog, new LastTeamStandingCondition(), new Random(1), heroes);

            RoundResult result = resolver.Resolve(state, new[]
            {
                new TurnPlan(1, new[] { new UnitPlan(1, new UnitOrder[] { new CastOrder("call", P(2, 1)) }) })
            });

            Assert.IsNull(state.GetUnit(3));
            Assert.AreEqual(6, caster.Mana, "Un sort impossible ne coûte pas de mana.");
            Assert.IsTrue(result.Beats[0].Events.Exists(e => e is SpellFizzledEvent));
        }

        // --- Réseau ---

        private sealed class Lobby
        {
            public LoopbackNetwork Network = new LoopbackNetwork();
            public GameServer Server;
            public List<NetworkMatchSession> Sessions = new List<NetworkMatchSession>();
            public List<LoopbackClient> Transports = new List<LoopbackClient>();
        }

        private Lobby NewLobby()
        {
            var lobby = new Lobby();
            lobby.Server = new GameServer(
                lobby.Network,
                new ServerSettings { PlayersPerTeam = 1, PlanningSeconds = 10f },
                spells,
                new Random(4),
                characters);
            return lobby;
        }

        private NetworkMatchSession Join(Lobby lobby, string characterId)
        {
            LoopbackClient transport = lobby.Network.CreateClient();
            var session = new NetworkMatchSession(transport, spells, "P", characterId, characters);
            lobby.Sessions.Add(session);
            lobby.Transports.Add(transport);
            transport.Connect();
            return session;
        }

        private static UnitState OwnUnit(NetworkMatchSession session)
        {
            foreach (UnitState unit in session.State.Units)
            {
                if (unit.PlayerId == session.LocalPlayerId)
                {
                    return unit;
                }
            }

            return null;
        }

        [Test]
        public void Network_TheServerCreatesEachPlayerWithTheCharacterTheyChose()
        {
            Lobby lobby = NewLobby();
            NetworkMatchSession a = Join(lobby, "mage");
            NetworkMatchSession b = Join(lobby, "knight");

            UnitState first = OwnUnit(a);
            UnitState second = OwnUnit(b);

            Assert.AreEqual("mage", first.CharacterId);
            Assert.AreEqual("knight", second.CharacterId);
            Assert.AreEqual(22, first.MaxHealth);
            Assert.AreEqual(40, second.MaxHealth);
            Assert.IsTrue(first.KnowsSpell("fireball"));
            Assert.IsTrue(b.State.GetUnit(second.Id).KnowsSpell("sword_strike"));
        }

        [Test]
        public void Network_AClientWithoutAChoiceGetsTheFirstPlayableCharacter()
        {
            Lobby lobby = NewLobby();
            NetworkMatchSession a = Join(lobby, "");
            Join(lobby, "archer");

            Assert.AreEqual("knight", OwnUnit(a).CharacterId);
        }

        [Test]
        public void Network_UnknownAndNonPlayableCharactersAreRefused()
        {
            Lobby lobby = NewLobby();
            Join(lobby, "dragon");
            Join(lobby, "totem");

            Assert.AreEqual(2, lobby.Network.DisconnectedByServer.Count);
            Assert.AreEqual(0, lobby.Server.QueuedPlayerCount);
            Assert.AreEqual(0, lobby.Server.ActiveMatchCount);
        }

        [Test]
        public void Network_AClientWithDifferentCharactersIsRefused()
        {
            Lobby lobby = NewLobby();

            var altered = new CharacterCatalog();
            foreach (string id in characters.AllIds)
            {
                CharacterDefinition c = characters.Get(id);
                altered.Add(new CharacterDefinition(c.Id, c.DisplayName, c.MaxHealth + 10, c.MaxMana, c.MovementPoints, c.SpellIds, c.IsPlayable));
            }

            LoopbackClient transport = lobby.Network.CreateClient();
            var session = new NetworkMatchSession(transport, spells, "Triche", "knight", altered);
            string failure = null;
            session.Failed += text => failure = text;
            transport.Connect();

            Assert.IsNotNull(failure);
            Assert.Contains(transport.Id, lobby.Network.DisconnectedByServer);
        }

        [Test]
        public void Network_ClientsReceiveTheCharacterIdOfEveryUnit_IncludingSummons()
        {
            Lobby lobby = NewLobby();
            NetworkMatchSession a = Join(lobby, "archer");
            NetworkMatchSession b = Join(lobby, "mage");

            UnitState own = OwnUnit(a);
            var target = new GridPosition(own.Position.X + (own.Position.X < 6 ? 2 : -2), own.Position.Y);
            a.TrySubmitPlan(
                new TurnPlan(a.LocalPlayerId, new[] { new UnitPlan(own.Id, new UnitOrder[] { new CastOrder("totem", target) }) }),
                out _);
            b.TrySubmitPlan(new TurnPlan(b.LocalPlayerId, new UnitPlan[0]), out _);

            foreach (NetworkMatchSession session in lobby.Sessions)
            {
                Assert.AreEqual("archer", session.State.GetUnit(own.Id).CharacterId);
                Assert.AreEqual("totem", session.State.GetUnit(3).CharacterId);
                Assert.AreEqual("mage", session.State.GetUnit(2).CharacterId);
            }
        }

        [Test]
        public void Network_ALongNameIsTruncatedInsteadOfBreakingTheMessage()
        {
            string longName = new string('é', 100);
            var message = new HelloMessage(NetProtocol.Version, longName, 1, "knight");

            var decoded = (HelloMessage)MessageCodec.Decode(MessageCodec.Encode(message));

            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(decoded.PlayerName), NetLimits.MaxStringBytes);
            Assert.IsTrue(decoded.PlayerName.StartsWith("é"));
            Assert.AreEqual("knight", decoded.CharacterId);
        }

        [Test]
        public void Network_ALongUnitNameSurvivesTheSnapshot()
        {
            MatchState state = MatchFactory.CreateTeamMatch(new MatchConfig(), characters, 1);
            state.AddUnit(new UnitState(
                9, 0, 0, new string('x', 200), P(5, 5), 10, 4, 0, new string[0], true, 1, "totem"));

            var decoded = (MatchStartedMessage)MessageCodec.Decode(
                MessageCodec.Encode(new MatchStartedMessage(0, 10f, MatchSnapshot.From(state))));

            UnitState rebuilt = decoded.Snapshot.CreateState().GetUnit(9);
            Assert.LessOrEqual(rebuilt.Name.Length, NetLimits.MaxStringBytes);
            Assert.AreEqual("totem", rebuilt.CharacterId);
        }
    }
}
